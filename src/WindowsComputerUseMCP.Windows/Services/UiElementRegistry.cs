using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// UI Automation要素を elementId (GUID) で参照できるようにするためのプロセス内キャッシュ。
/// UIA3のCOMオブジェクトはプロセス生存中のみ有効という前提でシンプルな辞書実装とする。
/// 上限を超えた場合は古いものから破棄する（FIFO）。
/// </summary>
internal sealed class UiElementRegistry
{
    private const int MaxCachedElements = 2000;

    private readonly ConcurrentDictionary<string, AutomationElement> _elements = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();

    public string Register(AutomationElement element)
    {
        var id = Guid.NewGuid().ToString("N");
        _elements[id] = element;
        _insertionOrder.Enqueue(id);

        while (_insertionOrder.Count > MaxCachedElements && _insertionOrder.TryDequeue(out var oldest))
        {
            _elements.TryRemove(oldest, out _);
        }

        return id;
    }

    public bool TryGet(string elementId, out AutomationElement? element) => _elements.TryGetValue(elementId, out element);
}
