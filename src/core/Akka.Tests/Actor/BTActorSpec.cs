//-----------------------------------------------------------------------
// <copyright file="FSMActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    public class BTActorSpec : AkkaSpec
    {
        #region Actors

        public class ConstructorIncrement : BT<AtomicCounter>
        {
            public ConstructorIncrement(AtomicCounter counter, TestLatch latch)
            {
                counter.IncrementAndGet();
                latch.CountDown();
            }
        }

        public class ExecuteIncrement : BT<AtomicCounter>
        {
            public ExecuteIncrement(AtomicCounter counter, TestLatch latch)
            {
                StartWith(Execute(cxt =>
                {
                    cxt.GlobalData.IncrementAndGet();
                }), counter);

                latch.CountDown();
            }
        }

        public class OneEcho : BT<object>
        {
            public OneEcho()
            {
                StartWith(ReceiveAny(
                    Execute(ctx =>
                    {
                        Sender.Tell(ctx.CurrentMessage);
                    })), null);
            }
        }

        public class NestedEcho : BT<object>
        {
            public NestedEcho()
            {
                StartWith(
                    ReceiveAny(
                        ReceiveAny(
                            Execute(ctx =>
                            {
                                Sender.Tell(ctx.CurrentMessage);
                            }))), null);
            }
        }

        public class SequenceOne : BT<List<string>>
        {
            public SequenceOne(List<string> pipe, TestLatch latch)
            {
                StartWith(Sequence(Execute(ctx => ctx.GlobalData.Add("1"))), pipe);

                latch.CountDown();
            }
        }

        public class SequenceTwo : BT<List<string>>
        {
            public SequenceTwo(List<string> pipe, TestLatch latch)
            {
                StartWith(Sequence(
                    Execute(ctx => ctx.GlobalData.Add("1")),
                    Execute(ctx => ctx.GlobalData.Add("2"))), pipe);

                latch.CountDown();
            }
        }

        public class SequenceReceive : BT<List<string>>
        {
            public SequenceReceive(List<string> pipe, TestLatch latch)
            {
                StartWith(Sequence(
                    Execute(ctx => ctx.GlobalData.Add("1")),
                    ReceiveAny(Execute(ctx => ctx.GlobalData.Add(ctx.CurrentMessage as string))),
                    Execute(ctx => latch.CountDown())), pipe);
            }
        }

        public class SequenceReceiveSequenceReceive : BT<List<string>>
        {
            public SequenceReceiveSequenceReceive(List<string> pipe, TestLatch latch)
            {
                StartWith(Sequence(
                    ReceiveAny(Execute(ctx => ctx.GlobalData.Add(ctx.CurrentMessage as string))),
                    Execute(ctx => ctx.GlobalData.Add("2")),
                    Sequence(),
                    Sequence(
                        ReceiveAny(Sequence(
                            Execute(ctx => ctx.GlobalData.Add(ctx.CurrentMessage as string)),
                            ReceiveAny(Execute(ctx => ctx.GlobalData.Add(ctx.CurrentMessage as string)))
                            ))
                        ),
                    Execute(ctx => latch.CountDown())), pipe);
            }
        }

        public class LoopEcho : BT<object>
        {
            public LoopEcho()
            {
                StartWith(Loop(
                    Sequence(
                        ReceiveAny(Execute(ctx => Sender.Tell(ctx.CurrentMessage))),
                        ReceiveAny(Execute(ctx => Sender.Tell(ctx.CurrentMessage))))), null);
            }
        }

        #endregion

        [Fact]
        public void BTActor_ConstructorIncrement()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new ConstructorIncrement(counter, latch)));

            latch.Ready();
            Assert.Equal(1, counter.Current);
        }

        [Fact]
        public void BTActor_ExecuteIncrement()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new ExecuteIncrement(counter, latch)));

            latch.Ready();
            Assert.Equal(1, counter.Current);
        }

        [Fact]
        public void BTActor_OneEcho_Exactly_One()
        {
            var bt = Sys.ActorOf<OneEcho>();

            bt.Tell("echo1", TestActor);

            ExpectMsg((object)"echo1");

            bt.Tell("echo1", TestActor);

            ExpectNoMsg(100);
        }

        [Fact]
        public void BTActor_NestedEcho_Second()
        {
            var bt = Sys.ActorOf<NestedEcho>();

            bt.Tell("echo1", TestActor);
            bt.Tell("echo2", TestActor);
            ExpectMsg((object)"echo2");

            bt.Tell("echo3", TestActor);

            ExpectNoMsg(100);
        }

        [Fact]
        public void BTActor_SequenceOne()
        {
            var pipe = new List<string>();
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceOne(pipe, latch)));

            latch.Ready();

            Assert.Equal(new[] { "1" }, pipe);
        }

        [Fact]
        public void BTActor_SequenceTwo()
        {
            var pipe = new List<string>();
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceTwo(pipe, latch)));

            latch.Ready();

            Assert.Equal(new[] { "1", "2" }, pipe);
        }

        [Fact]
        public void BTActor_SequenceReceive()
        {
            var pipe = new List<string>();
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceReceive(pipe, latch)));

            bt.Tell("2");

            latch.Ready();

            Assert.Equal(new[] { "1", "2" }, pipe);
        }

        [Fact]
        public void BTActor_SequenceReceiveSequenceReceive()
        {
            var pipe = new List<string>();
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceReceiveSequenceReceive(pipe, latch)));

            bt.Tell("1");
            bt.Tell("3");
            bt.Tell("4");

            latch.Ready();

            Assert.Equal(new[] { "1", "2", "3", "4" }, pipe);
        }

        [Fact]
        public void BTActor_Loop100()
        {
            var bt = Sys.ActorOf(Props.Create(() => new LoopEcho()));

            foreach (var i in Enumerable.Range(1, 100))
            {
                bt.Tell(i.ToString(), TestActor);
            }

            foreach (var i in Enumerable.Range(1, 100))
            {
                ExpectMsg(i.ToString());
            }
        }
    }
}
