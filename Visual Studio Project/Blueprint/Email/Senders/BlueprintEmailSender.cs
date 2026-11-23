// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Email.Senders
{
    /// <summary>Defines how to send a Blueprint email message.</summary>
    public abstract class BlueprintEmailSender
    {
        /// <summary>Sends a message and returns when the email transport accepts it.</summary>
        /// <param name="email">The message to send.</param>
        public abstract void Send(BlueprintEmail email);

        /// <summary>Submits a message to the email transport asynchronously.</summary>
        /// <param name="email">The message to send.</param>
        /// <returns>A task that completes when the transport accepts the message.</returns>
        public abstract Task SendAsync(BlueprintEmail email);
    }
}
