// This program and the accompanying materials are made available under the terms of the
//  Eclipse Public License v2.0 which accompanies this distribution, and is available at
//  https://www.eclipse.org/legal/epl-v20.html
//  
//  SPDX-License-Identifier: EPL-2.0

using CrossSlash;
using Ficedula;
using Ficedula.FF7.Exporters;
using System.Runtime.InteropServices;
using System.Windows;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        System.Globalization.CultureInfo.CurrentCulture =
            System.Globalization.CultureInfo.CurrentUICulture =
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        if (args.Length == 0) {
            var app = new Application();
            app.Run(new SplashWindow());
            return;
        }

        AttachConsoleOrCreateOne();
        RunCommandLine(args);
    }

    private static void RunCommandLine(string[] args) {
        switch (args.Length) {
            case 1:
            case 2:
                if (ExportKind.Exporters.TryGetValue(args[0], out var helpExporter)) {
                    Console.WriteLine(helpExporter.Help);
                } else {
                    Console.WriteLine("USAGE: CrossSlash [ExportType] [SourceLGPOrFolder] [OutputFile] [parameters...]");
                    Console.WriteLine("ExportTypes: " + string.Join(", ", ExportKind.Exporters.Keys));
                    Console.WriteLine("Use CrossSlash [ExportType] for help on a specific exporter");
                }
                break;

            default:
                if (!ExportKind.Exporters.TryGetValue(args[0], out var exporter))
                    throw new ArgumentException($"Unknown export type: {args[0]}");
                var source = DataSource.Create(args[1]);
                try {
                    string dest = args[2];
                    var options = args.Skip(2)
                        .Where(s => s.StartsWith('/'))
                        .Select(s => s.Substring(1).Split(':'))
                        .ToDictionary(
                            sa => sa[0],
                            sa => sa.ElementAtOrDefault(1) ?? "true",
                            StringComparer.InvariantCultureIgnoreCase
                        );
                    var parameters = args.Skip(3).Where(s => !s.StartsWith('/'));
                    Serialisation.SetProperties(exporter.Config, options);
                    exporter.Execute(source, dest, parameters);
                } finally {
                    if (source is IDisposable disposable)
                        disposable.Dispose();
                }
                Console.WriteLine("Done");
                break;
        }
    }

    private static void AttachConsoleOrCreateOne() {
        if (!AttachConsole(AttachParentProcess))
            AllocConsole();

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}
