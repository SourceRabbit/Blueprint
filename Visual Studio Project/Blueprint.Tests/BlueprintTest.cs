// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace Blueprint.Tests
{
    /// <summary>Defines the entry point and shared assertion helpers for a Blueprint test.</summary>
    public abstract class BlueprintTest
    {
        // Home Database
        protected readonly string fDatabaseConnectionString = "Server=.\\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

        // Work Database
       // protected readonly string fDatabaseConnectionString = "Server=.;Integrated Security = True; Encrypt=True;TrustServerCertificate=True;";
        private int fAssertionCount;

        /// <summary>Runs the checks and manages the resources of this test.</summary>
        /// <exception cref="Exception">An assertion or test resource operation fails.</exception>
        public abstract void RunTest();

        /// <summary>Returns the number of successful assertions in the current run.</summary>
        /// <returns>The number of assertions that completed successfully.</returns>
        public int getAssertionCount()
        {
            return fAssertionCount;
        }

        /// <summary>Resets the assertion count before a new test run.</summary>
        protected void ResetAssertions()
        {
            fAssertionCount = 0;
        }

        /// <summary>Stops the test when the expected behavior is violated.</summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="message">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        protected void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            fAssertionCount++;
        }

        /// <summary>Verifies that an unsupported operation fails with the expected exception type.</summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The operation to execute.</param>
        /// <param name="message">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The expected exception was not thrown.</exception>
        protected void AssertThrows<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                fAssertionCount++;
                return;
            }
            throw new InvalidOperationException(message);
        }
    }
}
