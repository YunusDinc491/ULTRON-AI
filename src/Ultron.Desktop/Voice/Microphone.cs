using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ultron.Desktop.Voice;

/// <summary>
/// Hangi mikrofon kullanılacak (2026-10-30, kullanıcı: "çağrı kelimesiyle seslenince bağırmam gerekiyor, sonrasında fısıltıyı bile duyuyor").
/// Eskiden çağrı kelimesi dinleyicisi de kayıt da listenin ilk cihazını alıyordu; dinleyici bunu Ultron açılırken bir kez seçip saatlerce
/// tutuyordu (o an ilk sırada laptopun dahili mikrofonu olabiliyordu), kayıt ise her seferinde yeniden seçiyordu. Artık ikisi de
/// Windows'ta VARSAYILAN seçili mikrofonu kullanır; bulunamazsa ilk cihaz.
/// </summary>
public static class Microphone
{
    /// <summary>Windows'un varsayılan kayıt cihazının WaveIn numarası ve adı.</summary>
    public static (int Number, string Name) Pick()
    {
        var count = WaveInEvent.DeviceCount;
        if (count == 0)
            return (-1, "mikrofon yok");
        string? preferred = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            preferred = device.FriendlyName;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
        {
            // Varsayılan cihaz okunamadı: ilk cihaz.
        }
        var names = Enumerable.Range(0, count).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();
        return WakeWordRules.ChooseMicrophone(names, preferred);
    }
}
