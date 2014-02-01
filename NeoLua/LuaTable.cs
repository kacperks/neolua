﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Neo.IronLua
{
  ///////////////////////////////////////////////////////////////////////////////
  /// <summary></summary>
  public class LuaTable : IDynamicMetaObjectProvider, INotifyPropertyChanged, IEnumerable<KeyValuePair<object, object>>
  {
    [ThreadStatic]
    private static PropertyInfo piItemIndex = null;
    [ThreadStatic]
    private static MethodInfo miCheckMethodVersion = null;
    [ThreadStatic]
    private static MethodInfo miSetValue1 = null;
    [ThreadStatic]
    private static MethodInfo miSetValue2 = null;

    #region -- enum MemberAccessFlag --------------------------------------------------

    ///////////////////////////////////////////////////////////////////////////////
    /// <summary></summary>
    [Flags]
    protected enum MemberAccessFlag
    {
      /// <summary>A normal get expression.</summary>
      None = 0,
      /// <summary>Get the expression for write access.</summary>
      ForWrite = 1,
      /// <summary>Get the expression for member access.</summary>
      MemberInvoke = 2,
      /// <summary>Member name is not case sensitive.</summary>
      IgnoreCase = 4
    } // enum MemberAccessFlag

    #endregion

    #region -- class LuaMetaObject ----------------------------------------------------

    ///////////////////////////////////////////////////////////////////////////////
    /// <summary></summary>
    private class LuaMetaObject : DynamicMetaObject
    {
      /// <summary></summary>
      /// <param name="value"></param>
      /// <param name="expression"></param>
      public LuaMetaObject(LuaTable value, Expression expression)
        : base(expression, BindingRestrictions.Empty, value)
      {
      } // ctor

      /// <summary></summary>
      /// <param name="binder"></param>
      /// <param name="indexes"></param>
      /// <param name="value"></param>
      /// <returns></returns>
      public override DynamicMetaObject BindSetIndex(SetIndexBinder binder, DynamicMetaObject[] indexes, DynamicMetaObject value)
      {
        Expression exprSet = Expression.Convert(value.Expression, typeof(object));

        if (indexes.Length == 1)
        {
          // the index is normaly an expression --> call setvalue
          return new DynamicMetaObject(
            Expression.Block(
              SetValueExpression(Expression.Convert(Expression, typeof(LuaTable)), Expression.Convert(indexes[0].Expression, typeof(object)), exprSet),
              exprSet
            ),
            BindingRestrictions.GetInstanceRestriction(Expression, Value));
        }
        else
        {
          Expression[] args = new Expression[indexes.Length];

          // Convert the indexes
          for (int i = 0; i < indexes.Length; i++)
            args[i] = Expression.Convert(indexes[i].Expression, typeof(object));

          return new DynamicMetaObject(
            Expression.Block(
              SetValueExpression(Expression.Convert(Expression, typeof(LuaTable)), args, exprSet),
              exprSet
            ),
            BindingRestrictions.GetInstanceRestriction(Expression, Value));
        }
      } // func BindSetIndex

      /// <summary></summary>
      /// <param name="binder"></param>
      /// <param name="indexes"></param>
      /// <returns></returns>
      public override DynamicMetaObject BindGetIndex(GetIndexBinder binder, DynamicMetaObject[] indexes)
      {
        if (indexes.Length == 1)
        {
          // the index is normaly an expression
          return new DynamicMetaObject(
            Expression.Call(
              Expression.Convert(Expression, typeof(LuaTable)),
              typeof(LuaTable).GetMethod("GetValue", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(object) }, null),
              Expression.Convert(indexes[0].Expression, typeof(object))
            ),
            BindingRestrictions.GetInstanceRestriction(Expression, Value));
        }
        else
        {
          Expression[] args = new Expression[indexes.Length];

          // Convert the indexes
          for (int i = 0; i < indexes.Length; i++)
            args[i] = Expression.Convert(indexes[i].Expression, typeof(object));

          return new DynamicMetaObject(
            Expression.Call(
              Expression.Convert(Expression, typeof(LuaTable)),
              typeof(LuaTable).GetMethod("GetValue", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(object[]) }, null),
              Expression.NewArrayInit(typeof(object), args)
            ),
            BindingRestrictions.GetInstanceRestriction(Expression, Value));
        }
      } // func BindGetIndex

      /// <summary></summary>
      /// <param name="binder"></param>
      /// <returns></returns>
      public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
      {
        return ((LuaTable)Value).GetMemberAccess(binder, Expression, binder.Name, binder.IgnoreCase ? MemberAccessFlag.ForWrite : MemberAccessFlag.None);
      } // func BindGetMember

      /// <summary></summary>
      /// <param name="binder"></param>
      /// <param name="value"></param>
      /// <returns></returns>
      public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
      {
        if (!value.HasValue)
          return binder.Defer(value);

        ParameterExpression tmp = Expression.Variable(typeof(object), "#tmp");
        DynamicMetaObject moGet = ((LuaTable)Value).GetMemberAccess(binder, Expression, binder.Name, MemberAccessFlag.ForWrite | (binder.IgnoreCase ? MemberAccessFlag.ForWrite : MemberAccessFlag.None));
        return new DynamicMetaObject(
          Expression.Block(new ParameterExpression[] { tmp },
            Expression.Assign(tmp, Expression.Convert(value.Expression, tmp.Type)),
            Expression.IfThen(Expression.NotEqual(tmp, moGet.Expression),
              Expression.Block(
                Expression.Assign(moGet.Expression, tmp),
                Expression.Call(Expression.Constant(Value, typeof(LuaTable)), typeof(LuaTable).GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod), Expression.Constant(binder.Name, typeof(string)))
              )
            ),
            tmp
          ), moGet.Restrictions);
      } // func BindSetMember

      /// <summary></summary>
      /// <param name="binder"></param>
      /// <param name="args"></param>
      /// <returns></returns>
      public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
      {
        LuaTable t = (LuaTable)Value;

        // Is there a member with this Name
        int iIndex = t.GetValueIndex(binder.Name, binder.IgnoreCase, false);
        if (iIndex >= 0 && t.IsIndexMarkedAsMethod(iIndex)) // check if the value is a method
        {
          // add the target and the self parameter
          Expression[] expanedArgs = new Expression[args.Length + 2];
          expanedArgs[0] = t.GetIndexAccess(iIndex);
          expanedArgs[1] = Expression;
          for (int i = 0; i < args.Length; i++)
            expanedArgs[i + 2] = args[i].Expression;

          Expression expr = Expression.Condition(
            t.CheckMethodVersionExpression(Expression),
            Expression.Dynamic(t.GetInvokeBinder(new CallInfo(args.Length + 1)), typeof(object), expanedArgs),
            binder.GetUpdateExpression(typeof(object)));

          return new DynamicMetaObject(expr, BindingRestrictions.GetInstanceRestriction(Expression, Value));
        }
        else // do a fallback to a normal invoke
        {
          DynamicMetaObject moGet = t.GetMemberAccess(binder, Expression, binder.Name, MemberAccessFlag.MemberInvoke | (binder.IgnoreCase ? MemberAccessFlag.IgnoreCase : MemberAccessFlag.None));
          Expression[] exprArgs = new Expression[args.Length + 1];
          exprArgs[0] = moGet.Expression;
          for (int i = 0; i < args.Length; i++)
            exprArgs[i + 1] = args[i].Expression;

          return new DynamicMetaObject(Expression.Dynamic(t.GetInvokeBinder(binder.CallInfo), typeof(object), exprArgs), moGet.Restrictions);
        }
      } // BindInvokeMember

      /// <summary></summary>
      /// <returns></returns>
      public override IEnumerable<string> GetDynamicMemberNames()
      {
        LuaTable t = (LuaTable)Value;
        foreach (var c in t.names.Keys)
          if (c is string)
            yield return (string)c;
      } // func GetDynamicMemberNames
    } // class LuaMetaObject

    #endregion

    /// <summary>Value has changed.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    private List<int> methods = null;             // Contains the indexes, they are method declarations
    private List<object> values = null;           // Array with values
    private Dictionary<object, int> names = null; // Names or Indices in the value-Array
    private int iLength = 0;

    private int iMethodVersion = 0;   // if the methods-array is changed, then this values gets increased
    private Dictionary<CallInfo, CallSiteBinder> invokeBinder = new Dictionary<CallInfo, CallSiteBinder>();

    #region -- Ctor/Dtor --------------------------------------------------------------

    /// <summary>Creates a new lua table</summary>
    public LuaTable()
    {
      this.methods = new List<int>();
      this.values = new List<object>();
      this.names = new Dictionary<object, int>();
    } // ctor

    #endregion

    #region -- IDynamicMetaObjectProvider members -------------------------------------

    /// <summary>Returns the Meta-Object</summary>
    /// <param name="parameter"></param>
    /// <returns></returns>
    public DynamicMetaObject GetMetaObject(Expression parameter)
    {
      return new LuaMetaObject(this, parameter);
    } // func GetMetaObject

    #endregion

    #region -- Dynamic Members --------------------------------------------------------

    /// <summary>Override to manipulate the member access.</summary>
    /// <param name="binder">Binder for the process.</param>
    /// <param name="exprTable">Expression for the binding process.</param>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="flags">Flags for the bind expression.</param>
    /// <returns>MO</returns>
    protected virtual DynamicMetaObject GetMemberAccess(DynamicMetaObjectBinder binder, Expression exprTable, object memberName, MemberAccessFlag flags)
    {
      // Get the index of the name
      int iIndex = GetValueIndex(memberName, (flags & MemberAccessFlag.IgnoreCase) != 0, (flags & MemberAccessFlag.ForWrite) != 0);

      if (iIndex == -1) // Create an update rule
      {
        // no fallback, to hide the static typed interface
        // if the length of the value-Array changed, then rebind
        Expression expr = Expression.Condition(
          TableChangedExpression(),
          Expression.Constant(null, typeof(object)),
          binder.GetUpdateExpression(typeof(object)));

        return new DynamicMetaObject(expr, BindingRestrictions.GetInstanceRestriction(exprTable, this));
      }
      else if ((flags & MemberAccessFlag.MemberInvoke) != 0)
      {
        Expression expr = Expression.Condition(
          CheckMethodVersionExpression(exprTable),
          GetIndexAccess(iIndex),
          binder.GetUpdateExpression(typeof(object)));

        return new DynamicMetaObject(expr, BindingRestrictions.GetInstanceRestriction(exprTable, this));
      }
      else
      {
        // Create MO with restriction
        return new DynamicMetaObject(GetIndexAccess(iIndex), BindingRestrictions.GetInstanceRestriction(exprTable, this));
      }
    } // func GetMemberAccess

    private Expression GetIndexAccess(int iIndex)
    {
      if (piItemIndex == null)
        piItemIndex = typeof(List<object>).GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);

      // IndexAccess expression
      return Expression.MakeIndex(Expression.Constant(values, typeof(List<object>)), piItemIndex, new Expression[] { Expression.Constant(iIndex, typeof(int)) });
    } // func GetIndexAccess

    private Expression TableChangedExpression()
    {
      return Expression.Equal(
        Expression.Property(Expression.Constant(values, typeof(List<object>)), typeof(List<object>), "Count"),
        Expression.Constant(values.Count, typeof(int)));
    } // func TableChangedExpression

    private Expression CheckMethodVersionExpression(Expression exprTable)
    {
      if (miCheckMethodVersion == null)
        miCheckMethodVersion = typeof(LuaTable).GetMethod("CheckMethodVersion", BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Instance);

      return Expression.Convert(Expression.Call(Expression.Convert(exprTable, typeof(LuaTable)), miCheckMethodVersion, Expression.Constant(iMethodVersion, typeof(int))), typeof(bool));
    } // func CheckMethodVersionExpression

    private CallSiteBinder GetInvokeBinder(CallInfo callInfo)
    {
      CallSiteBinder b;
      lock (invokeBinder)
        if (!invokeBinder.TryGetValue(callInfo, out b))
          b = invokeBinder[callInfo] = new Lua.LuaInvokeBinder(callInfo);
      return b;
    } // func GetInvokeBinder

    #endregion

    #region -- RegisterFunction, UnregisterFunction -----------------------------------

    /// <summary></summary>
    /// <param name="sName"></param>
    /// <param name="function"></param>
    public void RegisterFunction(string sName, Delegate function)
    {
      if (String.IsNullOrEmpty(sName))
        throw new ArgumentNullException("name");
      if (function == null)
        throw new ArgumentNullException("function");

      this[sName] = function;
    } // proc RegisterFunction

    #endregion

    #region -- GetValue, SetValue -----------------------------------------------------

    /// <summary>Notify property changed</summary>
    /// <param name="sPropertyName">Name of property</param>
    protected void OnPropertyChanged(string sPropertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(sPropertyName));
    } // proc OnPropertyChanged

    private int GetValueIndex(object item, bool lIgnoreCase, bool lCanCreateIndex)
    {
      int iIndex = -1;

      // Lookup the name in the hash-table
      if (lIgnoreCase && item is string)
      {
        foreach (var c in names)
        {
          if (c.Key is string && string.Compare((string)c.Key, (string)item, true) == 0)
          {
            iIndex = c.Value;
            break;
          }
        }
      }
      else if (!names.TryGetValue(item, out iIndex))
        iIndex = -1;

      // No index in the hash-table, can we create one
      if (iIndex == -1 && lCanCreateIndex)
      {
        names[item] = iIndex = values.Count;
        values.Add(null);

        // Update length
        int iNameIndex = item is int ? (int)item : -1;
        if (iNameIndex == -1)
          iLength = -1; // no array, length seem's not defined
        else
        {
          if (iLength == iNameIndex)
            iLength++;
          else
            iLength = -1; // no sequence
        }
      }

      return iIndex;
    } // func GetValueIndex

    private bool SetIndexValue(int iIndex, object value, bool lMarkAsMethod)
    {
      object c = values[iIndex];
      if (!Object.Equals(c, value))
      {
        // Mark methods
        int iMarkIndex = methods.BinarySearch(iIndex);
        if (lMarkAsMethod)
        {
          if (iMarkIndex < 0)
          {
            methods.Insert(~iMarkIndex, iIndex);
            iMethodVersion++;
          }
        }
        else
        {
          if (iMarkIndex >= 0)
          {
            methods.RemoveAt(iMarkIndex);
            iMethodVersion++;
          }
        }

        // set the value
        values[iIndex] = value;
        return true;
      }
      else
        return false;
    } // proc SetIndexValue

    private object GetValue(object item)
    {
      // Search the name in the hash-table
      int iIndex = GetValueIndex(item, false, false);
      return iIndex >= 0 ? values[iIndex] : null;
    } // func GetValue

    private object GetValue(object[] items)
    {
      return GetValue(items, 0);
    } // func GetValue

    private object GetValue(object[] items, int iIndex)
    {
      object o = GetValue(items[iIndex]);
      if (iIndex == items.Length - 1)
        return o;
      else
      {
        LuaTable t = o as LuaTable;
        if (t == null)
          return null;
        else
          return t.GetValue(items, iIndex++);
      }
    } // func GetValue

    private void SetValue(object item, object value, bool lMarkAsMethod)
    {
      // Get the Index for the value, if the value is null then do not create a new value
      int iIndex = GetValueIndex(item, false, value != null);

      // Set the value, if there is a index
      if (iIndex != -1 && SetIndexValue(iIndex, value, lMarkAsMethod))
      {
        // Notify property changed
        string sPropertyName = item as string;
        if (sPropertyName != null)
          OnPropertyChanged(sPropertyName);
      }
    } // proc SetValue

    private void SetValue(object[] items, object value)
    {
      SetValue(items, 0, value);
    } // func SetValue

    private void SetValue(object[] items, int iIndex, object value)
    {
      if (iIndex == items.Length - 1)
      {
        SetValue(items[iIndex], value, false);
      }
      else
      {
        int i = GetValueIndex(items[iIndex], false, true);
        LuaTable t = values[i] as LuaTable;
        if (t == null)
        {
          t = new LuaTable();
          values[i] = t;
        }
        t.SetValue(items, iIndex++, values);
      }
    } // func SetValue

    internal object SetMethod(string sMethodName, Delegate method)
    {
      SetValue(sMethodName, method, true);
      return method;
    } // proc SetMethod

    internal bool CheckMethodVersion(int iLastVersion)
    {
      return iMethodVersion == iLastVersion;
    } // func CheckMethodVersion

    internal bool IsIndexMarkedAsMethod(int iIndex)
    {
      return methods.BinarySearch(iIndex) >= 0;
    } // func IsIndexMarkedAsMethod

    /// <summary>Checks if the Member exists.</summary>
    /// <param name="sName">Membername</param>
    /// <param name="lIgnoreCase"></param>
    /// <returns></returns>
    public bool ContainsKey(string sName, bool lIgnoreCase = false)
    {
      return GetValueIndex(sName, lIgnoreCase, false) != -1;
    } // func ContainsKey

    #endregion

    #region -- Expressions ------------------------------------------------------------

    internal static Expression SetValueExpression(Expression table, Expression index, Expression set)
    {
      if (miSetValue1 == null)
        miSetValue1 = typeof(LuaTable).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(object), typeof(object), typeof(bool) }, null);

      return Expression.Call(
                table,
                miSetValue1,
                index,
                set,
                Expression.Constant(false)
              );
    } // func SetValueExpression

    internal static Expression SetValueExpression(Expression table, Expression[] indexes, Expression set)
    {
      if (miSetValue2 == null)
        miSetValue2 = typeof(LuaTable).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(object[]), typeof(object) }, null);
      return Expression.Call(
                table,
                miSetValue2,
                Expression.NewArrayInit(typeof(object), indexes),
                set
              );
    } // func SetValueExpression

    #endregion

    #region -- IEnumerator members ----------------------------------------------------

    /// <summary></summary>
    /// <returns></returns>
    public IEnumerator<KeyValuePair<object, object>> GetEnumerator()
    {
      foreach (var c in names)
      {
        var v = values[c.Value];
        if (v != null)
          yield return new KeyValuePair<object, object>(c.Key, v);
      }
    } // func IEnumerator<KeyValuePair<object, object>>

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    } // func System.Collections.IEnumerable.GetEnumerator

    #endregion

    /// <summary>Returns or sets an value in the lua-table.</summary>
    /// <param name="iIndex">Index.</param>
    /// <returns>Value or <c>null</c></returns>
    public object this[int iIndex] { get { return GetValue(iIndex); } set { SetValue(iIndex, value, false); } }
    /// <summary>Returns or sets an value in the lua-table.</summary>
    /// <param name="sName">Index.</param>
    /// <returns>Value or <c>null</c></returns>
    public object this[string sName] { get { return GetValue(sName); } set { SetValue(sName, value, false); } }
    /// <summary>Returns or sets an value in the lua-table.</summary>
    /// <param name="item">Index.</param>
    /// <returns>Value or <c>null</c></returns>
    public object this[object item] { get { return GetValue(item); } set { SetValue(item, value, false); } }

    /// <summary>Length if it is an array.</summary>
    public int Length { get { return iLength; } }
  } // class LuaTable
}
