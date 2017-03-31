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

        protected TreeMachine.ReceiveAnyWF ReceiveAny(TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveAnyWF(child);

        protected TreeMachine.SequenceWF Sequence(params TreeMachine.IWorkflow[] children)
            => new TreeMachine.SequenceWF(children);

        protected TreeMachine.LoopWF Loop(TreeMachine.IWorkflow child)
            => new TreeMachine.LoopWF(child);

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
                return _scopes.Select(s => s.ProcessMessage(new WFContext(Data, message))).ToList().Any();
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

                public object CurrentMessage { get; }

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

                public bool IsComplete => Status == WorkflowStatus.Success || Status == WorkflowStatus.Failure;

                public abstract void Reset();
            }

            public class ScopeWF : WFBase, ITransfer
            {
                private IWorkflow _child;
                private Stack<IWorkflow> _stack = new Stack<IWorkflow>();

                public ScopeWF(IWorkflow child)
                {
                    _child = child;
                    Status = _child.Status;
                }
                public IWorkflow Child => _child;

                public override void Reset()
                {

                }

                public override void Run(IContext context)
                {
                    bool done = false;
                    do
                    {
                        Status = _child.Status;

                        if (IsComplete)
                        {
                            if (_stack.Count > 0)
                            {
                                _child = _stack.Pop();
                            }
                            else
                            {
                                done = true;
                            }
                        }
                        else
                        {
                            if (!(_child is IReceive))
                            {
                                _child.Run(context);

                                var decorator = _child as IDecorator;
                                if (decorator != null)
                                {
                                    if (decorator.Status == WorkflowStatus.Running)
                                    {
                                        _stack.Push(decorator);

                                        _child = decorator.Next();
                                    }
                                }
                            }
                            else
                            {
                                done = true;
                            }
                        }
                    } while (!done);
                }

                public bool ProcessMessage(IContext context)
                {
                    if (IsComplete)
                        return false;

                    var receiver = _child as IReceive;

                    if (receiver != null)
                    {
                        if (receiver.ProcessMessage(context.CurrentMessage))
                        {
                            if (!IsComplete)
                            {
                                _stack.Push(_child);
                                _child = receiver.Child;

                                Run(context);
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

                    if (!IsComplete)
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

                object CurrentMessage { get; }

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

            public interface ISplit : IWorkflow
            {
                IWorkflow[] Children { get; }
            }

            public interface ITransfer : IWorkflow
            {
                IWorkflow Child { get; }
            }

            public interface ISpawn : ITransfer
            {
            }

            public interface IBecome : ITransfer
            {
            }

            public interface IReceive : ITransfer
            {
                bool ProcessMessage(object message);
            }

            public interface IReceive<T> : IReceive
            {
                bool ProcessMessage(T message);
            }
        }
    }
}
