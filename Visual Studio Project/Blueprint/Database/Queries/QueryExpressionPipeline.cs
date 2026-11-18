// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;

namespace Blueprint.Database.Queries
{
    /// <summary>Εφαρμόζει διαδοχικούς μετασχηματισμούς σε μια έκφραση, με τη σειρά καταχώρισης των visitors.</summary>
    internal sealed class QueryExpressionPipeline
    {
        private readonly List<Func<ExpressionVisitor>> fVisitorFactories;

        /// <summary>Δημιουργεί ένα κενό pipeline, έτοιμο για καταχώριση visitors.</summary>
        internal QueryExpressionPipeline()
        {
            fVisitorFactories = new List<Func<ExpressionVisitor>>();
        }

        /// <summary>Προσθέτει έναν μετασχηματισμό στο τέλος της ακολουθίας χωρίς να τον εκτελεί.</summary>
        /// <param name="visitorFactory">Η συνάρτηση που πρέπει να δημιουργεί νέο visitor σε κάθε εφαρμογή του pipeline.</param>
        /// <exception cref="ArgumentNullException">Η συνάρτηση δημιουργίας είναι null.</exception>
        internal void AddVisitor(Func<ExpressionVisitor> visitorFactory)
        {
            ArgumentNullException.ThrowIfNull(visitorFactory);
            fVisitorFactories.Add(visitorFactory);
        }

        /// <summary>Περνά την έκφραση από κάθε visitor, δίνοντας το αποτέλεσμα του προηγούμενου στον επόμενο.</summary>
        /// <param name="expression">Η αρχική έκφραση προς μετασχηματισμό.</param>
        /// <returns>Η τελική έκφραση ή η αρχική όταν δεν έχουν καταχωριστεί visitors.</returns>
        /// <exception cref="ArgumentNullException">Η αρχική έκφραση είναι null.</exception>
        /// <exception cref="InvalidOperationException">Μια συνάρτηση δημιουργίας επιστρέφει null ή ένας visitor αφαιρεί την αρχική έκφραση.</exception>
        internal Expression Apply(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            Expression transformedExpression = expression;
            foreach (Func<ExpressionVisitor> visitorFactory in fVisitorFactories)
            {
                ExpressionVisitor visitor = visitorFactory() ?? throw new InvalidOperationException("The visitor factory returned null.");
                transformedExpression = visitor.Visit(transformedExpression) ?? throw new InvalidOperationException("The expression visitor returned a null root expression.");
            }
            return transformedExpression;
        }
    }
}
