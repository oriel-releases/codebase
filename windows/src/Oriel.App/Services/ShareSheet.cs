using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace Oriel.UI.Services;

/// <summary>
/// Opens the Windows share sheet (the same one Edge and Photos use): Nearby sharing, Mail, Teams, and the apps the
/// person has installed. An unpackaged app has to ask for it through the window's handle.
/// </summary>
static class ShareSheet
{
    static readonly Guid DataTransferManagerId = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <returns>False when the sheet couldn't be opened, so the caller can fall back to copying the link.</returns>
    public static bool Show(nint window, string title, string text, Uri link)
    {
        if (window == 0) return false;
        try
        {
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var abi = interop.GetForWindow(window, DataTransferManagerId);
            var manager = MarshalInterface<DataTransferManager>.FromAbi(abi);

            void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= OnRequested;
                var data = args.Request.Data;
                data.Properties.Title = title;
                data.Properties.Description = text;
                data.SetText(text);
                data.SetWebLink(link);
            }

            manager.DataRequested += OnRequested;
            interop.ShowShareUIForWindow(window);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        nint GetForWindow(nint appWindow, in Guid riid);
        void ShowShareUIForWindow(nint appWindow);
    }
}
