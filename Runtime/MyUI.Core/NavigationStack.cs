using System.Collections.Generic;

namespace MyUI.Core
{
    /// <summary>
    /// 导航栈（QFramework UIKit.Stack 思路）：记录打开历史（serialId）。
    /// 由调用方 / 门面显式 Push（通常打开新面板前压入旧面板）；门面 Back() 关闭当前顶层面板、
    /// 逐层回退，栈记录在此面板关闭时自动清理（Remove 同时清掉其上方的记录）。
    /// Pop() 保留为显式弹出 API（需要手动管理栈时使用）。
    /// </summary>
    public sealed class NavigationStack
    {
        private readonly List<int> _stack = new List<int>();

        public int Count => _stack.Count;

        /// <summary>
        /// 压入一条返回入口。allowDuplicate=true 用于允许多个面板登记同一个下层父面板，
        /// 例如同时从主界面打开两个新页面时，每个页面都需要一条独立返回记录。
        /// </summary>
        public void Push(int serialId, bool allowDuplicate = false)
        {
            if (!allowDuplicate && _stack.Count > 0 && _stack[_stack.Count - 1] == serialId)
            {
                return; // 防重复压栈
            }

            _stack.Add(serialId);
        }

        /// <summary>查看栈顶；空栈返回 -1。</summary>
        public int Peek()
        {
            return _stack.Count > 0 ? _stack[_stack.Count - 1] : -1;
        }

        /// <summary>弹出并返回栈顶；空栈返回 -1。</summary>
        public int Pop()
        {
            if (_stack.Count == 0)
            {
                return -1;
            }

            int top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            return top;
        }

        public void Clear()
        {
            _stack.Clear();
        }

        /// <summary>面板关闭时清理：移除该 id 及其上方的所有记录。</summary>
        public void Remove(int serialId)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i] == serialId)
                {
                    _stack.RemoveRange(i, _stack.Count - i);
                    return;
                }
            }
        }
    }
}
