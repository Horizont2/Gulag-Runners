using System;
using System.Collections.Generic;
using NomadUI.Core;

namespace NomadUI.Navigation
{
    public class UINavigationStack
    {
        private readonly Stack<NavigationEntry> _stack = new Stack<NavigationEntry>();
        private readonly Action<UIView> _onHideRequested;

        public UINavigationStack(Action<UIView> onHideRequested)
        {
            _onHideRequested = onHideRequested;
        }

        public void Push(NavigationEntry entry)
        {
            // Opcjonalnie: ukryj poprzedni ekran, jeśli nowy to Screen, a nie Modal (do decyzji w UIPlatform)
            _stack.Push(entry);
        }

        public void Replace(NavigationEntry entry)
        {
            if (_stack.Count > 0)
            {
                var popped = _stack.Pop();
                _onHideRequested?.Invoke(popped.View);
            }
            _stack.Push(entry);
        }

        public NavigationEntry Pop()
        {
            if (_stack.Count == 0) return null;
            
            var current = _stack.Pop();
            _onHideRequested?.Invoke(current.View);
            
            return _stack.Count > 0 ? _stack.Peek() : null;
        }

        public bool HasEntries => _stack.Count > 0;
        
        public NavigationEntry Peek()
        {
            return _stack.Count > 0 ? _stack.Peek() : null;
        }

        public void Clear()
        {
            while (_stack.Count > 0)
            {
                var entry = _stack.Pop();
                _onHideRequested?.Invoke(entry.View);
            }
        }
    }
}
