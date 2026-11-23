// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Net.Mime;

namespace Blueprint.Email
{
    /// <summary>A named file included with an outgoing email message.</summary>
    public sealed class BlueprintEmailAttachment
    {
        /// <summary>Creates an attachment with its own copy of the file contents.</summary>
        /// <param name="fileName">The file name shown to recipients.</param>
        /// <param name="content">The file contents.</param>
        /// <param name="contentType">The MIME media type of the file.</param>
        public BlueprintEmailAttachment(string fileName, ReadOnlySpan<byte> content, string contentType = MediaTypeNames.Application.Octet)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
            FileName = fileName;
            Content = content.ToArray();
            ContentType = contentType;
        }

        /// <summary>The file name shown to recipients.</summary>
        public string FileName { get; }

        /// <summary>The file contents.</summary>
        public ReadOnlyMemory<byte> Content { get; }

        /// <summary>The MIME media type of the file.</summary>
        public string ContentType { get; }
    }
}
