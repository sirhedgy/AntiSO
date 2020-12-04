
using System.Collections.Generic;

namespace AntiSO.Infrastructure
{
    public abstract class SimpleRecursionRunner<TCallParams, TResult>
    {
        protected TResult _lastReturnValue;

        protected TResult RunRecursion(TCallParams callParams)
        {
            var stack = new Stack<IEnumerator<TCallParams>>();
            var curState = ComputeImpl(callParams);
            while (true)
            {
                if (curState.MoveNext())
                {
                    // We have an inner call
                    var innerCall = curState.Current;
                    stack.Push(curState);
                    curState = ComputeImpl(innerCall);
                }
                else
                {
                    // We have run out of inner calls and finished the current call
                    // so get back to the parent call.
                    if (!stack.TryPop(out curState))
                        break;
                }
            }

            return _lastReturnValue;
        }

        protected abstract IEnumerator<TCallParams> ComputeImpl(TCallParams callParams);

    }
}
