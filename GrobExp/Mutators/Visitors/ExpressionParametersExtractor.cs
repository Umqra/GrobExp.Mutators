﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace GrobExp.Mutators.Visitors
{
	public class ExpressionParametersExtractor : ExpressionVisitor
	{
		public ExpressionParametersExtractor(Expression parametersAccessor)
		{
			this.parametersAccessor = parametersAccessor;
			paramsIndex = 0;
		}

		public Expression ExtractParameters(Expression expression, out object[] parameters)
		{
			var result = Visit(expression);
			parameters = new object[hashtable.Count];
			foreach(DictionaryEntry entry in hashtable)
			{
				parameters[(int)entry.Value] = ((ExpressionWrapper)entry.Key).Expression;
			}
			return result;
		}

		public override Expression Visit(Expression node)
		{
			if(!node.IsLinkOfChain(true, true))
			{
				return base.Visit(node);
			}
			var key = new ExpressionWrapper(node, false);
			var index = hashtable[key];
			if(index == null)
			{
				hashtable[key] = index = paramsIndex++;
			}
			return Expression.Convert(Expression.ArrayIndex(parametersAccessor, Expression.Constant(index, typeof(int))), node.Type);
		}

		private readonly Expression parametersAccessor;
		private int paramsIndex;
		private readonly Hashtable hashtable = new Hashtable();
	}
}
