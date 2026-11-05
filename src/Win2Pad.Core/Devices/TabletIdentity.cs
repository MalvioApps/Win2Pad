using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Win2Pad.Core.Devices;

/// <summary>What a connected tablet says about itself over Bluetooth.</summary>
public sealed record TabletInfo(string? Name, string? Manufacturer, string? ModelIdentifier);

public static class TabletIdentity
{
    private static readonly Guid ManufacturerName = BluetoothUuidHelper.FromShortId(0x2A29);
    private static readonly Guid ModelNumber = BluetoothUuidHelper.FromShortId(0x2A24);

    /// <summary>
    /// Reads the tablet's Device Information service as a GATT client. The tablet is connected to
    /// our peripheral already, so this rides the same link; an iPad answers with "Apple Inc." and
    /// an identifier such as "iPad7,12". Returns null when the device cannot be opened.
    /// </summary>
    public static async Task<TabletInfo?> ReadAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        using var device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(cancellationToken);
        if (device is null) return null;

        string? manufacturer = null, model = null;
        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.DeviceInformation, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken);
        if (services.Status == GattCommunicationStatus.Success)
        {
            foreach (var service in services.Services)
            {
                using (service)
                {
                    manufacturer ??= await ReadStringAsync(service, ManufacturerName, cancellationToken);
                    model ??= await ReadStringAsync(service, ModelNumber, cancellationToken);
                }
            }
        }

        return new TabletInfo(device.Name, manufacturer, model);
    }

    /// <summary>
    /// Removes the Windows side of the Bluetooth pairing. The tablet keeps its own record until
    /// the user forgets the device there too. Returns a short description of the outcome.
    /// </summary>
    public static async Task<string> UnpairAsync(string deviceId)
    {
        var info = await DeviceInformation.CreateFromIdAsync(deviceId);
        if (!info.Pairing.IsPaired) return "not paired";
        var result = await info.Pairing.UnpairAsync();
        return result.Status switch
        {
            DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired => "unpaired",
            DeviceUnpairingResultStatus.AccessDenied => "Windows refused to unpair it",
            _ => $"unpairing failed ({result.Status})"
        };
    }

    private static async Task<string?> ReadStringAsync(GattDeviceService service, Guid uuid, CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0) return null;

        var read = await result.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (read.Status != GattCommunicationStatus.Success) return null;

        var bytes = new byte[read.Value.Length];
        DataReader.FromBuffer(read.Value).ReadBytes(bytes);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
    }
}
