using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace Klementina
{
    /// The Windows share sheet (Nearby Share, Mail, apps…) — the Windows counterpart of AirDrop.
    static class ShareOps
    {
        [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        static readonly Guid IidDataTransferManager = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");

        static R Wait<R>(IAsyncOperation<R> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new Exception("share: " + op.Status);
            return op.GetResults();
        }

        public static OpResult Share(List<string> files)
        {
            var items = new List<IStorageItem>();
            foreach (var f in files) items.Add(Wait(StorageFile.GetFileFromPathAsync(f)));

            // the share sheet needs a window to belong to: a tiny invisible one near the cursor
            var host = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Size = new System.Drawing.Size(1, 1), Location = Cursor.Position, Opacity = 0,
            };
            host.Show();
            Ui.ForceForeground(host.Handle);

            var interop = (IDataTransferManagerInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(DataTransferManager));
            Guid iid = IidDataTransferManager;
            IntPtr p = interop.GetForWindow(host.Handle, ref iid);
            var dtm = (DataTransferManager)Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);

            string title = files.Count == 1 ? Path.GetFileName(files[0]) : S.F("files_n", files.Count);
            dtm.DataRequested += (sender, args) =>
            {
                args.Request.Data.Properties.Title = title;
                args.Request.Data.SetStorageItems(items);
            };
            interop.ShowShareUIForWindow(host.Handle);

            // keep the host alive while the sheet is open, then tidy up
            var t = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); host.Close(); };
            t.Start();
            return new OpResult { Quiet = true };
        }
    }
}
