// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Entities.Triggers
{
    /// <summary>A scalar input on the maintained row or one of its direct references.</summary>
    internal sealed record ValueInput(PropertyInfo? Navigation, PropertyInfo Property);

    /// <summary>A stored decimal formula shared by trigger generation and dependency ordering.</summary>
    internal sealed class ValueTrigger
    {
        private readonly LambdaExpression fFormula;

        internal ValueTrigger(string name, Type entity, string target, LambdaExpression formula)
        {
            Name = name;
            Entity = entity;
            Target = target;
            fFormula = formula;
            HashSet<ValueInput> inputs = new HashSet<ValueInput>();
            Translate(input =>
            {
                inputs.Add(input);
                return input.Property.Name;
            });
            Inputs = inputs.ToArray();
        }

        /// <summary>Renders the validated arithmetic with physical column references supplied by the provider.</summary>
        internal string Translate(Func<ValueInput, string> column)
        {
            return Translate(fFormula.Body, column);
        }

        private string Translate(Expression expression, Func<ValueInput, string> column)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert } lift when Nullable.GetUnderlyingType(lift.Type) == lift.Operand.Type:
                    return Translate(lift.Operand, column);
                case ConstantExpression { Value: decimal number }:
                    return number.ToString("0.0###########################", CultureInfo.InvariantCulture);
                case MemberExpression { Member: PropertyInfo property } member when property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?):
                    if (member.Expression == fFormula.Parameters[0])
                    {
                        return column(new ValueInput(null, property));
                    }
                    if (member.Expression is MemberExpression { Member: PropertyInfo reference } navigation &&
                        navigation.Expression == fFormula.Parameters[0] && typeof(BlueprintEntity).IsAssignableFrom(reference.PropertyType))
                    {
                        return column(new ValueInput(reference, property));
                    }
                    break;
                case BinaryExpression binary:
                    string operation = binary.NodeType switch
                    {
                        ExpressionType.Add => "+",
                        ExpressionType.Subtract => "-",
                        ExpressionType.Multiply => "*",
                        ExpressionType.Divide => "/",
                        _ => throw new ArgumentException($"Unsupported operator in MaintainValue '{Name}': {binary.NodeType}.")
                    };
                    return $"({Translate(binary.Left, column)} {operation} {Translate(binary.Right, column)})";
            }
            throw new ArgumentException($"Unsupported expression in MaintainValue '{Name}': {expression}.");
        }

        internal string Name { get; }
        internal Type Entity { get; }
        internal string Target { get; }
        internal IReadOnlyList<ValueInput> Inputs { get; }
    }
}
