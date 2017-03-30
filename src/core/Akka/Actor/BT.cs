using System;
using System.Collections.Generic;

namespace Akka.Actor
{
    /// <summary>
    /// Behavior Tree Actor
    /// </summary>
    /// <typeparam name="TData">Global data object accessible to all scopes.</typeparam>
    public class BT<TData> : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            throw new System.NotImplementedException();
        }

        public TreeMachine.ActionWF Execute(Action<TreeMachine.IContext> action)
            => new TreeMachine.ActionWF(action);


        public class TreeMachine
        {
            private IBlackboard _rootBb;

            public TreeMachine(TData data, IBlackboard rootBb)
            {
                Data = data;
                _rootBb = rootBb;
            }

            public TData Data { get; }

            public abstract class WFBase : IWorkflow
            {
                public WorkflowStatus Status { get; protected set; }
                public object Result { get; protected set; }

                public abstract void Run(IContext context);
            }

            public class ActionWF : WFBase
            {
                private Action<IContext> _action;

                public ActionWF(Action<IContext> action)
                {
                    _action = action;
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

            public interface IBlackboard : IDictionary<object, object>
            {
                IReadOnlyCollection<object> Messages { get; }

                void PushMessage(object msg);
                object PopMessage();

                IBlackboard Insert();
                void Retract(IBlackboard bb);
            }

            public interface IContext
            {
                TData GlobalData { get; }

                IBlackboard ScopeData { get; }
            }

            public interface IWorkflow
            {
                WorkflowStatus Status { get; }

                void Run(IContext context);
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
            }
        }
    }
}
