// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CrossSlash {
    internal static class NativeFilePicker {
        private const uint ForceFileSystem = 0x00000040;
        private const uint FileMustExist = 0x00001000;
        private const uint PathMustExist = 0x00000800;
        private const uint NoChangeDirectory = 0x00000008;
        private const uint OverwritePrompt = 0x00000002;
        private const uint PickFolders = 0x00000020;
        private const int HResultCancelled = unchecked((int)0x800704C7);

        private static readonly Guid ShellItemGuid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        public static string? OpenLgp(string? currentPath, Window? owner = null) {
            var dialog = (IFileOpenDialog)new FileOpenDialogCom();
            try {
                dialog.SetTitle("Select an FF7 LGP archive");
                dialog.SetFileTypes(2, new[] {
                    new FilterSpec("LGP archives (*.lgp)", "*.lgp"),
                    new FilterSpec("All files (*.*)", "*.*"),
                });
                dialog.SetFileTypeIndex(1);
                SetInitialFolder(dialog, currentPath);
                SetOptions(dialog, ForceFileSystem | FileMustExist | PathMustExist | NoChangeDirectory);

                if (!Show(dialog, owner))
                    return null;

                dialog.GetResult(out IShellItem item);
                try {
                    return GetPath(item);
                } finally {
                    Release(item);
                }
            } finally {
                Release(dialog);
            }
        }

        public static string? OpenFolder(string title, string? currentPath, Window? owner = null) {
            var dialog = (IFileOpenDialog)new FileOpenDialogCom();
            try {
                dialog.SetTitle(title);
                SetInitialFolder(dialog, currentPath);
                SetOptions(dialog, ForceFileSystem | PathMustExist | PickFolders | NoChangeDirectory);

                if (!Show(dialog, owner))
                    return null;

                dialog.GetResult(out IShellItem item);
                try {
                    return GetPath(item);
                } finally {
                    Release(item);
                }
            } finally {
                Release(dialog);
            }
        }

        public static string? SaveGlb(string? currentPath, Window? owner = null) {
            var dialog = (IFileSaveDialog)new FileSaveDialogCom();
            try {
                dialog.SetTitle("Export GLB");
                dialog.SetFileTypes(1, new[] {
                    new FilterSpec("glTF binary (*.glb)", "*.glb"),
                });
                dialog.SetFileTypeIndex(1);
                dialog.SetDefaultExtension("glb");
                if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                    dialog.SetFileName(Path.GetFileName(currentPath));
                SetInitialFolder(dialog, currentPath);
                SetOptions(dialog, ForceFileSystem | PathMustExist | OverwritePrompt | NoChangeDirectory);

                if (!Show(dialog, owner))
                    return null;

                dialog.GetResult(out IShellItem item);
                try {
                    return GetPath(item);
                } finally {
                    Release(item);
                }
            } finally {
                Release(dialog);
            }
        }

        private static bool Show(IFileDialog dialog, Window? owner) {
            Window? dialogOwner = owner == null ? DesktopUi.MainWindow : DesktopUi.ResolveOwner(owner);
            IntPtr ownerHandle = dialogOwner == null ? IntPtr.Zero : new WindowInteropHelper(dialogOwner).Handle;
            int result = dialog.Show(ownerHandle);
            if (result == HResultCancelled)
                return false;

            Marshal.ThrowExceptionForHR(result);
            return true;
        }

        private static void SetOptions(IFileDialog dialog, uint optionsToAdd) {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | optionsToAdd);
        }

        private static void SetInitialFolder(IFileDialog dialog, string? path) {
            string initialDirectory = GetInitialDirectory(path);
            Guid shellItemGuid = ShellItemGuid;
            if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref shellItemGuid, out IShellItem folder) < 0)
                return;

            try {
                dialog.SetFolder(folder);
            } finally {
                Release(folder);
            }
        }

        private static string GetPath(IShellItem item) {
            item.GetDisplayName(0x80058000, out IntPtr pathPointer); // SIGDN_FILESYSPATH
            try {
                return Marshal.PtrToStringUni(pathPointer)
                    ?? throw new InvalidOperationException("The file picker did not return a filesystem path.");
            } finally {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        private static string GetInitialDirectory(string? path) {
            try {
                if (!string.IsNullOrWhiteSpace(path)) {
                    string fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                        return fullPath;

                    string? parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        return parent;
                }
            } catch {
                // Fall back to Documents when a remembered path is no longer valid.
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static void Release(object? value) {
            if (value != null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindingContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item
        );

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCom { }

        [ComImport]
        [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
        private class FileSaveDialogCom { }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog {
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport]
        [Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileSaveDialog : IFileDialog {
            void SetSaveAsItem(IShellItem item);
            void SetProperties(IntPtr properties);
            void SetCollectedProperties(IntPtr properties, [MarshalAs(UnmanagedType.Bool)] bool appendDefault);
            void GetProperties(out IntPtr properties);
            void ApplyProperties(IShellItem item, IntPtr properties, IntPtr parent, IntPtr progressSink);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem {
            void BindToHandler(IntPtr bindingContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint nameType, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FilterSpec {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Pattern;

            public FilterSpec(string name, string pattern) {
                Name = name;
                Pattern = pattern;
            }
        }
    }
}
