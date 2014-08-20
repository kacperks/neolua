﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IronLua;

namespace LuaDLR.Test
{
  ///////////////////////////////////////////////////////////////////////////////
  /// <summary></summary>
  [TestClass]
  public class LuaTypeTests : TestHelper
  {
    public class SubClass
    {
      public void Test()
      {
        Console.WriteLine("Empty");
      }

      public void Test(string a)
      {
        Console.WriteLine(a);
      }

      public void Test(string a, string b)
      {
        Console.WriteLine(a + b);
      }

      public void Fire()
      {
        if (EventTest != null)
          EventTest();
        if (EventTest2 != null)
          EventTest2(this, EventArgs.Empty);
      }

      public static int Value { get; set; }

      public event Action EventTest;
      public event EventHandler EventTest2;
    }

    public struct SubStruct
    {
      private int iValue;

      public SubStruct(int i)
      {
        iValue = i;
      }

      public int Value { get { return iValue; } set { iValue = value; } }
    }

    public static void Test()
    {
      Console.WriteLine("Hallo");
    }

		public static bool GreenColor(Color color)
		{
			Console.WriteLine("Color: {0}", color);
			return color == Color.Green;
		}

    [TestMethod]
    public void TypeTest01()
    {
      LuaType t = LuaType.GetType(typeof(Stream));
      Assert.IsTrue(t.Type != null);
      t = LuaType.GetType("System.Test.Test", false, true);
      Assert.IsTrue(t.Type == null);
      t = LuaType.GetType("LuaDLR.Test.LuaTypeTests.SubClass", false, true);
      Assert.IsTrue(t.Type != null);
      t = LuaType.GetType(typeof(List<string>));
      Assert.IsTrue(t.Type != null);
      t = LuaType.GetType(typeof(string[]));
      Assert.IsTrue(t.Type != null);
    }

    [TestMethod]
    public void TypeTest02()
    {

      using (Lua l = new Lua())
      {
        dynamic g = l.CreateEnvironment();

        Type t = typeof(SubClass);
        LuaType tl = LuaType.GetType(t);
        TestResult(g.dochunk("return clr.LuaDLR.Test.LuaTypeTests.SubClass"), tl);
        TestResult(g.dochunk("return clr.LuaDLR.Test.LuaTypeTests.SubClass:GetType()"), t);

        TestResult(g.dochunk("return clr.System.IO.Stream"), LuaType.GetType(typeof(Stream)));

        LuaType tNull = g.dochunk("return clr.System.Test.Test");
        Assert.IsTrue(tNull.Type == null);

        tl = g.dochunk("return clr.System.Collections.Generic.List[clr.System.String]", "test");
        TestResult(new LuaResult(tl.Type), typeof(List<string>));
        tl = g.dochunk("return clr.System.String[]", "test");
        TestResult(new LuaResult(tl.Type), typeof(string[]));
      }
    }

    [TestMethod]
    public void TypeTest03()
    {
      using (Lua l = new Lua())
      {
        dynamic g = l.CreateEnvironment();

        g.dochunk("return cast(System.IO.Stream, null);");
        g.dochunk("return cast(LuaDLR.Test.LuaTypeTests.SubClass, null);");
        g.dochunk("return cast(System.Collections.Generic.List[string[]], null);");
        g.dochunk("return cast(System.String[], null);");
        g.dochunk("return cast(string[], null);");
      }
    }

    [TestMethod]
    public void TypeTest04()
    {
      dynamic t = LuaType.GetType(typeof(int));
      Type t1 = t;
      Type t2 = (Type)t;
      Assert.IsTrue(t1 == typeof(int));
      Assert.IsTrue(t2 == typeof(int));
    }

    [TestMethod]
    public void TypeTest05()
    {
      TestCode(Lines(
        new string[]
        {
          "const StringBuilder typeof System.Text.StringBuilder;",
          "local sb : StringBuilder = StringBuilder();",
          "sb:Append('hallo');",
          "return sb:ToString();"
        }),
        "hallo");
    }

    [TestMethod]
    public void TypeTest06()
    {
      TestCode("clr.LuaDLR.Test.LuaTypeTests.SubClass.Value = 3;");
      Assert.IsTrue(SubClass.Value == 3);
    }

		[TestMethod]
		public void TypeTest07()
		{
			TestCode("return clr.LuaDLR.Test.LuaTypeTests:GreenColor(clr.System.Drawing.Color.Green);", true);
		}

		[TestMethod]
		public void PropertyTest01()
		{
			TestCode(Lines(
				"clr.LuaDLR.Test.LuaTypeTests.SubClass.Value = 1;",
				"clr.LuaDLR.Test.LuaTypeTests.SubClass.Value = 2;",
				"clr.LuaDLR.Test.LuaTypeTests.SubClass.Value = 1.0;",
				"return clr.LuaDLR.Test.LuaTypeTests.SubClass.Value;"), 1);
		}

    [TestMethod]
    public void MethodTest01()
    {
      using (Lua l = new Lua())
      {
        dynamic g = l.CreateEnvironment();
        g.console = LuaType.GetType(typeof(Console));
        g.dochunk("console.WriteLine('Hallo!');", "test");
        dynamic wl = g.console.WriteLine;
        Assert.IsTrue(wl.GetType() == typeof(LuaOverloadedMethod));
        int iCount = wl.Count;
        Assert.IsTrue(iCount == 19);
        for (int i = 0; i < wl.Count; i++)
        {
          if (i == 17)
            Console.WriteLine("VarArgs NotImplemented.");
          else
            Console.WriteLine("{0}: {1}", i, wl[i].GetType().Name);
        }
      }
    }

    [TestMethod]
    public void MethodTest02()
    {
      using (Lua l = new Lua())
      {
        dynamic g = l.CreateEnvironment();
        g.console = LuaType.GetType(typeof(Console));
        g.dochunk("console.WriteLine('Hallo!');", "test");
        dynamic wl = g.console.WriteLine;
        Delegate dlg1 = wl[LuaType.GetType(typeof(string))];
        Delegate dlg2 = g.dochunk("return console.WriteLine[clr.System.String]");
        Delegate dlg3 = g.dochunk("return console.WriteLine[]");
        Assert.IsTrue(dlg1 == dlg2);
        Assert.IsTrue(dlg3 != null);
      }
    }

    [TestMethod]
    public void MethodTest03()
    {
      using (Lua l = new Lua())
      {
        l.PrintExpressionTree = true;
        dynamic g = l.CreateEnvironment();
        g.console = LuaType.GetType(typeof(Console));
        g.dochunk("console.WriteLine('Hallo!');", "test");
        g.dochunk("c = console.WriteLine[clr.System.String];");
        //g.c = g.console.WriteLine[typeof(string)];
        g.c("Hallo");
        g.dochunk("c('Hallo!')");
      }
    }

    [TestMethod]
    public void MethodTest04()
    {
      using (Lua l = new Lua())
      {
        l.PrintExpressionTree = true;
        var g = l.CreateEnvironment();
        dynamic m = g.DoChunk("return clr.LuaDLR.Test.LuaTypeTests.Test", "dummy");
        MethodInfo mi = m;
        Action action = m;
        Delegate dlg = m;
        m();
        Assert.IsTrue(mi != null);
        Assert.IsTrue(action != null);
        Assert.IsTrue(dlg != null);
      }
    }

    [TestMethod]
    public void MethodTest05()
    {
      using (Lua l = new Lua())
      {
        dynamic g = l.CreateEnvironment();
        dynamic r = g.dochunk(Lines(
          new string[] {
            "local c = clr.LuaDLR.Test.LuaTypeTests.SubClass()",
            "c.Test('Test');",
            "return c.Test;"
          }));

        foreach (var c in r[0])
          Console.WriteLine(c.GetType().Name);
      }
    }

    [TestMethod]
    public void MethodTest06()
    {
      TestCode("return 'Hallo'.Substring(2);", "llo");
    } 

    [TestMethod]
    public void EventTest01()
    {
      SubClass c = new SubClass();
      c.EventTest += () => Console.WriteLine("Fired.");
      c.Fire();
    }

    [TestMethod]
    public void EventTest02()
    {
      TestCode(Lines(
        "const SubClass typeof LuaDLR.Test.LuaTypeTests.SubClass;",
        "local c : SubClass = SubClass();",
        "c.EventTest:add(function():void print('Fired.'); end);",
        "c:Fire();"));
    }

    [TestMethod]
    public void EventTest03()
    {
      TestCode(Lines(
        "const SubClass typeof LuaDLR.Test.LuaTypeTests.SubClass;",
        "local c : SubClass = SubClass();",
        "c.EventTest2:add(function(sender : object, e : System.EventArgs):void print('Fired.'); end);",
        "c:Fire();"));
    }

    [TestMethod]
    public void EventTest04()
    {
      using (Lua l = new Lua())
      {
        var g = l.CreateEnvironment();
        var c = l.CompileChunk(Lines(
          "local a : System.EventHandler = function(a, b) : void",
          "  print('Hallo');",
          "end;",
          "a()"), "dummy", true);
        g.DoChunk(c);
      }
    }

    [TestMethod]
    public void EventTest05()
    {
      using (Lua l = new Lua())
      {
        l.PrintExpressionTree = true;
        var g = l.CreateEnvironment();
        var c = l.CompileChunk(Lines(
          "local a : System.EventHandler = function(a, b) : void",
          "  print('Hallo');",
          "end;",
          "a();"), "dummy", false);
        g.DoChunk(c);
      }
    }

    [TestMethod]
    public void CtorTest01()
    {
      TestCode("return clr.LuaDLR.Test.LuaTypeTests.SubStruct().Value", 0);
    }

    [TestMethod]
    public void CtorTest02()
    {
      TestCode("return clr.LuaDLR.Test.LuaTypeTests.SubStruct(2).Value", 2);
    }

    [TestMethod]
    public void CtorTest03()
    {
      TestCode("return cast(LuaDLR.Test.LuaTypeTests.SubStruct, { Value = 2 }).Value", 2);
    }
  } // class LuaTypeTests 
}
