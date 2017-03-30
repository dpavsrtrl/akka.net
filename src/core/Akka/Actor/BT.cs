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

        public class TreeMachine
        {
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
