using System.Text.Json;

namespace MicToggle;

internal sealed class ChatGptVoiceModeAutoStarter
{
    private const int Pending = 0;
    private const int Running = 1;
    private const int Started = 2;

    private int _state = Pending;

    internal static string TryStartScript { get; } = """
        (() => {
          const normalize = value => (value || '').trim().toLowerCase();
          const isVisible = button => {
            const rect = button.getBoundingClientRect();
            const style = window.getComputedStyle(button);
            return rect.width > 0
              && rect.height > 0
              && style.display !== 'none'
              && style.visibility !== 'hidden';
          };
          const labelsVoice = label => label.includes('voice') || label.includes('음성');
          const labelsStart = label => label.includes('start') || label.includes('시작');
          const labelsEnd = label => label.includes('end')
            || label.includes('끝내기')
            || label.includes('종료');
          const labelsDictation = label => label.includes('dictation')
            || label.includes('받아쓰기');
          const buttons = Array.from(document.querySelectorAll('button'));
          const activeButton = buttons.find(button => {
            const label = normalize(button.getAttribute('aria-label'));
            return labelsVoice(label) && labelsEnd(label) && !labelsDictation(label);
          });
          if (activeButton) {
            return {
              started: true,
              clicked: false,
              label: activeButton.getAttribute('aria-label')
            };
          }

          const button = buttons.find(candidate => {
            const label = normalize(candidate.getAttribute('aria-label'));
            return labelsVoice(label)
              && labelsStart(label)
              && !labelsDictation(label)
              && !candidate.disabled
              && isVisible(candidate);
          });
          if (!button) {
            return { started: false, clicked: false };
          }

          const label = button.getAttribute('aria-label');
          button.click();
          return { started: true, clicked: true, label };
        })()
        """;

    internal static string TryStopScript { get; } = """
        (() => {
          const normalize = value => (value || '').trim().toLowerCase();
          const isVisible = button => {
            const rect = button.getBoundingClientRect();
            const style = window.getComputedStyle(button);
            return rect.width > 0
              && rect.height > 0
              && style.display !== 'none'
              && style.visibility !== 'hidden';
          };
          const labelsVoice = label => label.includes('voice') || label.includes('음성');
          const labelsEnd = label => label.includes('end')
            || label.includes('끝내기')
            || label.includes('종료');
          const labelsDictation = label => label.includes('dictation')
            || label.includes('받아쓰기');
          const button = Array.from(document.querySelectorAll('button')).find(candidate => {
            const label = normalize(candidate.getAttribute('aria-label'));
            return labelsVoice(label)
              && labelsEnd(label)
              && !labelsDictation(label)
              && !candidate.disabled
              && isVisible(candidate);
          });
          if (!button) {
            return { stopped: false, clicked: false };
          }

          const label = button.getAttribute('aria-label');
          button.click();
          return { stopped: true, clicked: true, label };
        })()
        """;

    public bool TryBegin()
    {
        return Interlocked.CompareExchange(ref _state, Running, Pending) == Pending;
    }

    public void Complete(bool started)
    {
        Volatile.Write(ref _state, started ? Started : Pending);
    }

    public bool Rearm()
    {
        var state = Volatile.Read(ref _state);
        if (state == Pending)
        {
            return true;
        }

        return state == Started
            && Interlocked.CompareExchange(ref _state, Pending, Started) == Started;
    }

    internal static bool DidStart(string resultJson)
    {
        return ReadBooleanProperty(resultJson, "started");
    }

    internal static bool DidStop(string resultJson)
    {
        return ReadBooleanProperty(resultJson, "stopped");
    }

    private static bool ReadBooleanProperty(string resultJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
