// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Selects which stored translation participates in an entity index.</summary>
    public enum EIndexLanguageScope
    {
        /// <summary>Index mapped scalar properties without resolving multilingual accessors.</summary>
        None,

        /// <summary>Use the stored default-language column for each multilingual property in the index.</summary>
        DefaultLanguage
    }
}
