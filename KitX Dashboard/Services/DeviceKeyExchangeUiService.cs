using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Views;

namespace KitX.Dashboard.Services;

/// <summary>
/// Dashboard implementation of the device key-exchange UI and the receive-side
/// coordinator. On the initiating side it displays the temporary password; on the
/// receive side it subscribes to <see cref="EventNames.OnReceiveExchangeDeviceKey"/>,
/// prompts the user for the password and drives the local <see cref="IDeviceServer"/>
/// accept/reject flow.
/// </summary>
public class DeviceKeyExchangeUiService : IDeviceKeyExchangeUi, IDisposable
{
    private readonly IEventService _eventService;
    private readonly IDeviceServer _devicesServer;
    private readonly EventHandler<ExchangeDeviceKeyEventArgs> _onReceiveExchangeDeviceKeyHandler;

    public DeviceKeyExchangeUiService(IEventService eventService, IDeviceServer devicesServer)
    {
        _eventService = eventService;
        _devicesServer = devicesServer;
        _onReceiveExchangeDeviceKeyHandler = OnReceiveExchangeDeviceKey;
        _eventService.Subscribe(EventNames.OnReceiveExchangeDeviceKey, _onReceiveExchangeDeviceKeyHandler);
    }

    /// <inheritdoc/>
    public IDisposable ShowPasswordForInitiator(string password)
    {
        var window = new ExchangeDeviceKeyWindow().DisplayVerificationCode(password);
        window.Show();
        return new WindowCloseHandle(window);
    }

    /// <inheritdoc/>
    public Task<string?> PromptForPasswordAsync(string requestingDeviceAddress, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetResult(null));

        var window = new ExchangeDeviceKeyWindow();

        window.OnVerificationCodeEntered(code =>
        {
            tcs.TrySetResult(code);
            window.Close();
        });

        window.OnCancel(() => tcs.TrySetResult(null));

        window.Show();

        return tcs.Task;
    }

    /// <summary>
    /// Receive side: a remote device requested a key exchange. Prompt the user for the
    /// temporary password (read from the initiating device's screen) and drive the local
    /// server accept/reject flow. Runs on the UI thread (a Window must be opened there).
    /// </summary>
    private void OnReceiveExchangeDeviceKey(object? sender, ExchangeDeviceKeyEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var password = await PromptForPasswordAsync(e.RequestingDeviceAddress);
            if (password is null)
                _devicesServer.RejectExchangeKey();
            else
                _devicesServer.AcceptExchangeKey(password);
        });
    }

    public void Dispose()
    {
        _eventService.Unsubscribe(EventNames.OnReceiveExchangeDeviceKey, _onReceiveExchangeDeviceKeyHandler);
    }

    private sealed class WindowCloseHandle : IDisposable
    {
        private readonly Avalonia.Controls.Window _window;

        public WindowCloseHandle(Avalonia.Controls.Window window) => _window = window;

        public void Dispose() => Dispatcher.UIThread.Post(_window.Close);
    }
}
