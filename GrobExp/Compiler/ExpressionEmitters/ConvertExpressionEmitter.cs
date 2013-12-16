using System;
using System.Linq.Expressions;

using GrEmit;

namespace GrobExp.Compiler.ExpressionEmitters
{
    internal class ConvertExpressionEmitter : ExpressionEmitter<UnaryExpression>
    {
        protected override bool Emit(UnaryExpression node, EmittingContext context, GroboIL.Label returnDefaultValueLabel, ResultType whatReturn, bool extend, out Type resultType)
        {
            GroboIL.Label operandIsNullLabel = context.CanReturn ? context.Il.DefineLabel("operandIsNull") : null;
            var operandIsNullLabelUsed = ExpressionEmittersCollection.Emit(node.Operand, context, operandIsNullLabel, ResultType.Value, extend, out resultType); // stack: [obj]
            if(operandIsNullLabelUsed)
                context.EmitReturnDefaultValue(resultType, operandIsNullLabel, context.Il.DefineLabel("operandIsNotNull"));
            if(resultType != node.Type && !(context.Options.HasFlag(CompilerOptions.UseTernaryLogic) && resultType == typeof(bool?) && node.Type == typeof(bool)))
            {
                if(node.Method != null)
                    context.Il.Call(node.Method);
                else
                {
                    switch(node.NodeType)
                    {
                    case ExpressionType.Convert:
                        context.EmitConvert(node.Operand.Type, node.Type); // stack: [(type)obj]
                        break;
                    case ExpressionType.ConvertChecked:
                        context.EmitConvert(node.Operand.Type, node.Type, true); // stack: [(type)obj]
                        break;
                    default:
                        throw new InvalidOperationException("Node type '" + node.NodeType + "' is not valid at this point");
                    }
                }
                resultType = node.Type;
            }
            return false;
        }
    }
}