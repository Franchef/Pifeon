using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Pifeon.Core.Abstractions;

[ExcludeFromCodeCoverage]
public record TransferItem(
    string RelativePath, // es. "foto.jpg" oppure "Progetto/Documenti/report.pdf"
    long FileSize,       // Dimensione in byte
    string FullPath      // Percorso locale assoluto su disco
);
