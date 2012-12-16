﻿using System;
using System.Linq.Expressions;

using GrEmit;

namespace GrobExp.ExpressionEmitters
{
    internal interface IExpressionEmitter
    {
        bool Emit(Expression node, EmittingContext context, GroboIL.Label returnDefaultValueLabel, bool returnByRef, bool extend, out Type resultType);
    }
}