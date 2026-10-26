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

    /// <summary>The link, with the photo itself when <paramref name="imagePath"/> is given (apps that take pictures get it too).</summary>
    /// <returns>False when the sheet couldn't be opened, so the caller can fall back to copying the link.</returns>
    public static bool Show(nint window, string title, string text, Uri link, string? imagePath = null)
    {
        if (window == 0) return false;
        try
        {
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var abi = interop.GetForWindow(window, DataTransferManagerId);
            var manager = MarshalInterface<DataTransferManager>.FromAbi(abi);

            async void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= OnRequested;
                var data = args.Request.Data;
                data.Properties.Title = title;
                data.Properties.Description = text;
                data.SetText($"{text} {link}");
                data.SetWebLink(link);
                if (imagePath is null) return;
                var deferral = args.Request.GetDeferral();
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
                    data.SetStorageItems([file]);
                    data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
                    data.Properties.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file);
                }
                catch (Exception)
                {
                    // The link alone, then.
                }
                finally
                {
                    deferral.Complete();
                }
            }

            manager.DataRequested += OnRequested;
            if (imagePath is not null)
            {
                // Most apps take only one thing from the sheet, and with a picture that's the picture: the words and the
                // link are on the clipboard too, to paste as its caption.
                var caption = new DataPackage();
                caption.SetText($"{text} {link}");
                Clipboard.SetContent(caption);
            }
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
