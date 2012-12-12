﻿using System;
using System.Linq.Expressions;

using GrobExp;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class TestLogical
    {
        [Test]
        public void TestLogical1()
        {
            Expression<Func<TestClassA, bool?>> exp = o => o.NullableBool;
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.AreEqual(true, compiledExp(new TestClassA { NullableBool = true }));
            Assert.AreEqual(false, compiledExp(new TestClassA { NullableBool = false }));
        }

        [Test]
        public void TestLogical2()
        {
            Expression<Func<TestClassA, bool?>> exp = o => !o.NullableBool;
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.AreEqual(false, compiledExp(new TestClassA { NullableBool = true }));
            Assert.AreEqual(true, compiledExp(new TestClassA { NullableBool = false }));
        }

        [Test]
        public void TestLogical3()
        {
            Expression<Func<TestClassA, bool?>> exp = o => o.B.X > 0;
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.AreEqual(false, compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
        }

        [Test]
        public void TestLogical4()
        {
            Expression<Func<TestClassA, bool?>> exp = o => !(o.B.X > 0);
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.AreEqual(true, compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
        }

        [Test]
        public void TestLogical5()
        {
            Expression<Func<TestClassA, bool?>> exp = o => o.B.X > 0 && o.A.X > 0;
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.IsNull(compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { A = new TestClassA { X = 1 }, B = new TestClassB { X = 1 } }));
        }

        [Test]
        public void TestLogical6()
        {
            Expression<Func<TestClassA, bool?>> exp = o => o.B.X > 0 || o.A.X > 0;
            Func<TestClassA, bool?> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsNull(compiledExp(null));
            Assert.IsNull(compiledExp(new TestClassA()));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.IsNull(compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.IsNull(compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { A = new TestClassA { X = -1 }, B = new TestClassB { X = -1 } }));
        }

        [Test]
        public void TestLogical7()
        {
            Expression<Func<TestClassA, bool>> exp = o => o.B.X > 0 || o.A.X > 0;
            Func<TestClassA, bool> compiledExp = LambdaCompiler.Compile(exp);
            Assert.IsFalse(compiledExp(null));
            Assert.IsFalse(compiledExp(new TestClassA()));
            Assert.IsFalse(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.IsFalse(compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.IsFalse(compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { A = new TestClassA { X = -1 }, B = new TestClassB { X = -1 } }));
        }

        [Test]
        public void TestLogical8()
        {
            Expression<Func<TestClassA, int>> exp = o => o.B.X > 0 || o.A.X > 0 ? 1 : 0;
            Func<TestClassA, int> compiledExp = LambdaCompiler.Compile(exp);
            Assert.AreEqual(0, compiledExp(null));
            Assert.AreEqual(0, compiledExp(new TestClassA()));
            Assert.AreEqual(0, compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.AreEqual(0, compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(1, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.AreEqual(0, compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(1, compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(0, compiledExp(new TestClassA { A = new TestClassA { X = -1 }, B = new TestClassB { X = -1 } }));
        }

        [Test]
        public void TestLogical9()
        {
            Expression<Func<TestClassA, int>> exp = o => o.F(o.B.X > 0 || o.A.X > 0);
            Func<TestClassA, int> compiledExp = LambdaCompiler.Compile(exp);
            Assert.AreEqual(0, compiledExp(null));
            Assert.AreEqual(0, compiledExp(new TestClassA()));
            Assert.AreEqual(0, compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.AreEqual(0, compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(1, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.AreEqual(0, compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(1, compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(0, compiledExp(new TestClassA { A = new TestClassA { X = -1 }, B = new TestClassB { X = -1 } }));
        }

        [Test]
        public void TestLogical10()
        {
            Expression<Func<TestClassA, bool?>> exp = o => o.B.X > 0 || o.A.X > 0;
            ParameterExpression var = Expression.Variable(typeof(bool));
            var body = Expression.Block(typeof(bool), new[] { var }, Expression.Assign(var, Expression.Convert(exp.Body, typeof(bool))), var);
            Func<TestClassA, bool> compiledExp = LambdaCompiler.Compile(Expression.Lambda<Func<TestClassA, bool>>(body, exp.Parameters));
            Assert.IsFalse(compiledExp(null));
            Assert.IsFalse(compiledExp(new TestClassA()));
            Assert.IsFalse(compiledExp(new TestClassA { B = new TestClassB() }));
            Assert.IsFalse(compiledExp(new TestClassA { B = new TestClassB { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { B = new TestClassB { X = 1 } }));
            Assert.IsFalse(compiledExp(new TestClassA { A = new TestClassA { X = -1 } }));
            Assert.AreEqual(true, compiledExp(new TestClassA { A = new TestClassA { X = 1 } }));
            Assert.AreEqual(false, compiledExp(new TestClassA { A = new TestClassA { X = -1 }, B = new TestClassB { X = -1 } }));
        }

        public struct TestStructA
        {
            public string S { get; set; }
            public TestStructB[] ArrayB { get; set; }
            public int? X { get; set; }
            public int Y { get; set; }
        }

        public struct TestStructB
        {
            public string S { get; set; }
        }

        private class TestClassA
        {
            public string S { get; set; }
            public TestClassA A { get; set; }
            public TestClassB B { get; set; }
            public TestClassB[] ArrayB { get; set; }
            public int[] IntArray { get; set; }
            public int? X;
            public Guid Guid = Guid.Empty;
            public Guid? NullableGuid;
            public bool? NullableBool;
            public int Y;
            public bool Bool;

            public int F(bool b)
            {
                return b ? 1 : 0;
            }
        }

        private class TestClassB
        {
            public int? F2(int? x)
            {
                return x;
            }

            public int? F( /*Qzz*/ int a, int b)
            {
                return b;
            }

            public string S { get; set; }

            public TestClassC C { get; set; }
            public int? X;
            public int Y;
        }

        private class TestClassC
        {
            public string S { get; set; }

            public TestClassD D { get; set; }

            public TestClassD[] ArrayD { get; set; }
        }

        private class TestClassD
        {
            public TestClassE E { get; set; }
            public TestClassE[] ArrayE { get; set; }
            public string Z { get; set; }

            public int? X { get; set; }

            public readonly string S;
        }

        private class TestClassE
        {
            public string S { get; set; }
            public int X { get; set; }
        }

    }
}