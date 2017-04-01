using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Actor
{
    /// <summary>
    /// Behavior Tree Actor
    /// </summary>
    /// <typeparam name="TData">Global data object accessible to all scopes.</typeparam>
    public class BT<TData> : UntypedActor
    {
        protected TreeMachine Machine { get; set; }

        protected override void OnReceive(object message)
        {
            if (!Machine.ProcessMessage(message))
                Unhandled(message);
        }

        protected TreeMachine.ActionWF Execute(Action<TreeMachine.IContext> action)
            => new TreeMachine.ActionWF(action);

        protected TreeMachine.ConditionWF Condition(Func<TreeMachine.IContext, bool> pred)
            => new TreeMachine.ConditionWF(pred);

        protected TreeMachine.ReceiveAnyWF ReceiveAny(TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveAnyWF(child);

        protected TreeMachine.ReceiveAnyWF ReceiveAny(Action<TreeMachine.IContext> action)
            => ReceiveAny(Execute(action));

        protected TreeMachine.SequenceWF Sequence(params TreeMachine.IWorkflow[] children)
            => new TreeMachine.SequenceWF(children);

        protected TreeMachine.LoopWF Loop(TreeMachine.IWorkflow child)
            => new TreeMachine.LoopWF(child);

        protected TreeMachine.ParallelWF Parallel(Func<IEnumerable<WorkflowStatus>, WorkflowStatus> statusEval, params TreeMachine.IWorkflow[] children)
            => new TreeMachine.ParallelWF(statusEval, children);

        protected void StartWith(TreeMachine.IWorkflow wf, TData data)
        {
            Machine = new TreeMachine(data, null);
            Machine.Run(wf);
        }

        public class TreeMachine
        {
            private List<ScopeWF> _scopes = new List<ScopeWF>();

            private IBlackboard _rootBb;

            public TreeMachine(TData data, IBlackboard rootBb)
            {
                Data = data;
                _rootBb = rootBb;
            }

            public bool ProcessMessage(object message)
            {
                return _scopes.Select(s => s.ProcessMessage(message)).ToList().Any();
            }

            public void Run(IWorkflow wf)
            {
                _scopes.Add(new ScopeWF(wf));
                Run();
            }

            public void Run()
            {
                _scopes.ForEach(s => s.Run(new WFContext(Data)));
            }

            protected class WFContext : IContext
            {
                public WFContext(TData data)
                {
                    GlobalData = data;
                }
                public WFContext(TData data, object message) : this(data)
                {
                    CurrentMessage = message;
                }

                public object CurrentMessage { get; set; }

                public TData GlobalData { get; }

                public IBlackboard ScopeData
                {
                    get
                    {
                        throw new NotImplementedException();
                    }
                }
            }
            public TData Data { get; }

            public abstract class WFBase : IWorkflow
            {
                public virtual WorkflowStatus Status { get; protected set; }
                public object Result { get; protected set; }

                public abstract void Run(IContext context);

                public bool IsCompleted => Status == WorkflowStatus.Success || Status == WorkflowStatus.Failure;

                public abstract void Reset();
            }

            public class ScopeWF : WFBase, ITransmit
            {
                private IWorkflow _current;
                private Stack<IWorkflow> _stack = new Stack<IWorkflow>();
                private IContext _context;

                public ScopeWF(IWorkflow child)
                {
                    _current = child;
                    Status = _current.Status;
                }
                public IWorkflow Child => _current;

                public bool RunAgain =>
                    _current != null && !(_current as ITransmit)?.RunAgain == false;

                public override void Reset()
                {

                }

                public override void Run(IContext context)
                {
                    if (IsCompleted)
                        return;

                    _context = context;

                    bool done = false;
                    do
                    {
                        Status = _current.Status;

                        if (IsCompleted)
                        {
                            if (_stack.Count > 0)
                            {
                                _current = _stack.Pop();
                            }
                            else
                            {
                                done = true;
                            }
                        }
                        else
                        {
                            if (!(_current is IReceive))
                            {
                                _current.Run(context);

                                var decorator = _current as IDecorator;
                                var transmitter = _current as ITransmit;

                                if (decorator != null)
                                {
                                    if (decorator.Status == WorkflowStatus.Running)
                                    {
                                        _stack.Push(decorator);

                                        _current = decorator.Next();
                                    }
                                }
                                else if (transmitter?.RunAgain == false && transmitter.Status.IsCompleted() == false)
                                {
                                    done = true;
                                }
                            }
                            else
                            {
                                done = true;
                            }

                        }
                    } while (!done);
                }

                public bool ProcessMessage(object message)
                {
                    _context.CurrentMessage = message;

                    if (IsCompleted)
                        return false;

                    var transmitter = _current as ITransmit;

                    if (transmitter != null)
                    {
                        if (transmitter.ProcessMessage(message))
                        {
                            if (!IsCompleted)
                            {
                                var receiver = transmitter as IReceive;

                                if (receiver != null)
                                {
                                    _stack.Push(_current);
                                    _current = receiver.Child;
                                }

                                Run(_context);
                            }

                            return true;
                        }
                    }

                    return false;
                }
            }

            public class ReceiveAnyWF : WFBase, IReceive<object>
            {
                private bool _hasProcessed;

                public ReceiveAnyWF(IWorkflow child)
                {
                    Child = child;
                }

                public override WorkflowStatus Status
                    => _hasProcessed ? Child?.Status ?? WorkflowStatus.Success : WorkflowStatus.Undetermined;

                public IWorkflow Child { get; }

                public bool RunAgain => false;

                public override void Reset()
                {
                    Child?.Reset();
                    _hasProcessed = false;
                }

                public override void Run(IContext context)
                {
                    throw new InvalidOperationException("Receive tasks don't run.");
                }

                public virtual bool ProcessMessage(object message)
                {
                    Status = Child?.Status ?? WorkflowStatus.Success;

                    _hasProcessed = true;

                    return _hasProcessed;
                }
            }

            public class ActionWF : WFBase
            {
                private Action<IContext> _action;

                public ActionWF(Action<IContext> action)
                {
                    _action = action;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    try
                    {
                        Status = WorkflowStatus.Running;

                        _action(context);

                        Status = WorkflowStatus.Success;
                    }
                    catch (Exception ex)
                    {
                        Status = WorkflowStatus.Failure;
                        Result = ex;
                    }
                }
            }

            public class ConditionWF : WFBase
            {
                private readonly Func<IContext, bool> _pred;

                public ConditionWF(Func<IContext, bool> pred)
                {
                    _pred = pred;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    try
                    {
                        Status = _pred(context)
                            ? WorkflowStatus.Success
                            : WorkflowStatus.Failure;
                    }
                    catch (Exception ex)
                    {
                        Status = WorkflowStatus.Failure;
                        Result = ex;
                    }
                }
            }

            public class SequenceWF : WFBase, IDecorator
            {
                private List<IWorkflow> _children;
                private IWorkflow _current;
                private int _pos = 0;

                public SequenceWF(params IWorkflow[] children)
                {
                    _children = new List<IWorkflow>(children);
                }

                public override void Reset()
                {
                    _pos = 0;
                    _children.ForEach(c => c.Reset());
                    Status = WorkflowStatus.Undetermined;
                }

                public IWorkflow Next()
                {
                    if (_pos < _children.Count)
                    {
                        _current = _children[_pos];
                        _pos++;
                    }
                    else
                    {
                        _current = null;
                    }

                    return _current;
                }

                public override void Run(IContext context)
                {
                    if (_current?.Status == WorkflowStatus.Failure)
                    {
                        Status = WorkflowStatus.Failure;

                        return;
                    }

                    if (!IsCompleted)
                    {
                        if (_pos >= _children.Count)
                        {
                            Status = WorkflowStatus.Success;
                        }
                        else
                        {
                            Status = WorkflowStatus.Running;
                        }
                    }
                }
            }

            public class LoopWF : WFBase, IDecorator
            {
                private IWorkflow _child;

                public LoopWF(IWorkflow child)
                {
                    _child = child;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _child?.Reset();
                }

                public IWorkflow Next()
                {
                    _child?.Reset();
                    return _child;
                }

                public override void Run(IContext context)
                {
                    if (_child?.Status == WorkflowStatus.Failure)
                    {
                        Status = WorkflowStatus.Failure;
                    }
                    else
                    {
                        Status = WorkflowStatus.Running;
                    }
                }
            }

            public class ParallelWF : WFBase, ITransmit
            {
                private readonly Func<IEnumerable<WorkflowStatus>, WorkflowStatus> _statusEval;
                private readonly List<ScopeWF> _children;

                public bool RunAgain =>
                    _children.Any(c => c.RunAgain);

                public ParallelWF(Func<IEnumerable<WorkflowStatus>, WorkflowStatus> statusEval, params IWorkflow[] children)
                {
                    _statusEval = statusEval;
                    _children = children.Select(c => new ScopeWF(c)).ToList();
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _children.ForEach(c => c.Reset());
                }

                public override void Run(IContext context)
                {
                    if (Status.IsCompleted())
                        return;

                    _children.ForEach(c => c.Run(context));

                    var states = _children.Select(c => c.Status).ToList();

                    var status = _statusEval(states);

                    if (!GetIsComplete(status) && (states.Count == 0 || states.All(GetIsComplete)))
                    {
                        Status = WorkflowStatus.Failure;
                    }
                    else
                    {
                        Status = status;
                    }
                }

                private static bool GetIsComplete(WorkflowStatus s)
                {
                    return s == WorkflowStatus.Failure || s == WorkflowStatus.Success;
                }

                public bool ProcessMessage(object message)
                {
                    return _children.Select(c => c.ProcessMessage(message)).ToList().Any();
                }
            }

            /// <summary>
            /// Blackboard allows extension. Extension emulates local scope.
            /// Extended blackboard can hide dictionary entries of previous scope blackboard.
            /// Lower scope blackboard cannot modify higher scope blackboard.
            /// </summary>
            public interface IBlackboard : IDictionary<object, object>
            {
                /// <summary>
                /// Concatenation of blackboard messages from all scopes.
                /// </summary>
                IReadOnlyList<object> Messages { get; }

                /// <summary>
                /// Prepend message.
                /// </summary>
                /// <param name="msg"></param>
                void PushMessage(object msg);

                /// <summary>
                /// Remove first message. Only local blackboard is affected.
                /// When local message list is empty, return null.
                /// </summary>
                /// <returns>Message object or null.</returns>
                object PopMessage();

                /// <summary>
                /// Extend blackboard for local modification.
                /// </summary>
                /// <returns>Extended blackboard.</returns>
                IBlackboard Extend();
                /// <summary>
                /// Discard extended blackboard.
                /// </summary>
                /// <returns>Previous blackboard.</returns>
                IBlackboard Retract();
            }

            public interface IContext
            {
                TData GlobalData { get; }

                object CurrentMessage { get; set; }

                IBlackboard ScopeData { get; }
            }

            public interface IWorkflow
            {
                WorkflowStatus Status { get; }

                object Result { get; }

                void Run(IContext context);

                void Reset();
            }

            public interface IAction : IWorkflow
            {
            }

            public interface IDecorator : IWorkflow
            {
                IWorkflow Next();
            }

            public interface ISplit : ITransmit
            {
                IWorkflow[] Children { get; }
            }

            public interface ITransmit : IWorkflow
            {
                bool RunAgain { get; }
                bool ProcessMessage(object message);

            }

            public interface IParent : IWorkflow
            {
                IWorkflow Child { get; }
            }

            public interface ISpawn : ITransmit
            {
            }

            public interface IBecome : ITransmit
            {
            }

            public interface IReceive : ITransmit, IParent
            {
            }

            public interface IReceive<T> : IReceive
            {
                bool ProcessMessage(T message);
            }
        }
    }

    /// <summary>
    /// Workflow Progress.
    /// </summary>
    public enum WorkflowStatus
    {
        Undetermined,
        Running,
        Success,
        Failure
    }

    public static class WorkflowEx
    {
        public static bool IsCompleted(this WorkflowStatus status)
            => status == WorkflowStatus.Failure || status == WorkflowStatus.Success;

        public static WorkflowStatus AllSucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcAllSucceed);
        }

        public static WorkflowStatus FirstSucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcFirstSucceed);
        }

        public static WorkflowStatus AnySucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcAnySucceed);
        }

        private static WorkflowStatus GetStatus(IEnumerable<WorkflowStatus> states, CalcStatus calc)
        {
            bool hasFailure = false;
            bool hasSuccess = false;
            bool hasRunning = false;
            bool hasUnknown = false;
            bool hasAny = false;

            foreach (var s in states)
            {
                hasFailure = s == WorkflowStatus.Failure;
                hasSuccess = s == WorkflowStatus.Success;
                hasRunning = s == WorkflowStatus.Running;
                hasUnknown = s == WorkflowStatus.Undetermined;
                hasAny = true;
            }

            return calc(hasFailure, hasSuccess, hasRunning, hasUnknown, hasAny);
        }

        private static WorkflowStatus CalcAllSucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return hasFailure
                ? WorkflowStatus.Failure
                : hasRunning || hasUnknown
                    ? WorkflowStatus.Running
                    : WorkflowStatus.Success;
        }

        private static WorkflowStatus CalcFirstSucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return !hasAny || hasFailure
                ? WorkflowStatus.Failure
                : hasSuccess
                    ? WorkflowStatus.Success
                    : WorkflowStatus.Running;
        }

        private static WorkflowStatus CalcAnySucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return !hasAny
                ? WorkflowStatus.Failure
                : hasSuccess
                    ? WorkflowStatus.Success
                    : hasRunning || hasUnknown
                        ? WorkflowStatus.Running
                        : WorkflowStatus.Failure;
        }

        private delegate WorkflowStatus CalcStatus(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUndetermined, bool hasAny);
    }
}
