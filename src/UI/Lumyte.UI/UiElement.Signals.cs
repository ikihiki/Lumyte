using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns reactive observers independently of replaceable declarations.</summary>
public partial class UiElement
{
    internal SignalObserver? Signals { get; set; }

    internal sealed class SignalObserver : ISignalScheduler, IDisposable
    {
        private readonly UiSurface _surface;
        private readonly UiElement _element;
        private readonly SignalScope _scope = new();
        private IReadOnlyDictionary<object, UiSignalArgument>? _arguments;
        private SignalReadSet? _reads;
        private SignalReadSet? _contentReads;
        private SignalEffect? _effect;
        private bool _admitted;
        private bool _disposed;
        private bool _changed;

        internal SignalObserver(UiSurface surface, UiElement element, Widget widget, SignalReadSet? contentReads)
        {
            _surface = surface;
            _element = element;
            _arguments = widget.SignalArguments;
            _reads = widget.ConstructionReads;
            _contentReads = contentReads;
            try
            {
                _scope.Run(() => _effect = Signal.Effect(Read, this, surface.FailSignalUpdate));
            }
            catch
            {
                _scope.Dispose();
                throw;
            }
        }

        internal bool ConstructionChanged { get; private set; }

        internal ulong Revision => _effect?.Revision ?? 0;

        public void Schedule(Action work) => _surface.PostSignal(this, work);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Dispose();
            _surface.ForgetSignalObserver(this);
            _arguments = null;
            _reads = null;
            _contentReads = null;
        }

        internal bool CanReuse(Widget next, SignalReadSet? contentReads)
        {
            if (!ReferenceEquals(_contentReads, contentReads) || !ReferenceEquals(_reads, next.ConstructionReads) || (_arguments?.Count ?? 0) != (next.SignalArguments?.Count ?? 0))
            {
                return false;
            }

            if (_arguments is not null)
            {
                foreach ((object key, UiSignalArgument argument) in _arguments)
                {
                    if (next.SignalArguments is not { } nextArguments || !nextArguments.TryGetValue(key, out UiSignalArgument? current) || !ReferenceEquals(argument.Source, current.Source))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal void Adopt(Widget next, SignalReadSet? contentReads)
        {
            _arguments = next.SignalArguments;
            _reads = next.ConstructionReads;
            _contentReads = contentReads;
            _changed = false;
            ConstructionChanged = false;
            _admitted = true;
            _surface.RecordSignalRevision(Revision);
            Signal.Untracked(() =>
            {
                Read();
                return true;
            });
        }

        private void Read()
        {
            if (_disposed)
            {
                return;
            }

            if (_arguments is not null)
            {
                foreach (UiSignalArgument argument in _arguments.Values)
                {
                    _changed |= argument.Read();
                }
            }

            if (_contentReads is not null)
            {
                _contentReads.Read();
                _changed |= !_contentReads.IsCurrent;
            }

            if (_reads is not null)
            {
                _reads.Read();
                ConstructionChanged |= !_reads.IsCurrent;
                _changed |= ConstructionChanged;
            }

            if (_changed && _admitted && ReferenceEquals(_element.Signals, this) && ReferenceEquals(_element.Surface, _surface))
            {
                _surface.QueueSignalElement(_element);
            }
        }
    }
}
