﻿//-----------------------------------------------------------------------
// <copyright file="FSMActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
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

        public class EmptyParallelOk : BT<AtomicCounter>
        {
            public EmptyParallelOk(AtomicCounter counter, TestLatch latch)
            {
                StartWith(Sequence(
                    Parallel(ss => ss.AllSucceed()),
                    Execute(ctx => latch.CountDown())), counter);
            }
        }

        public class SequenceParallelExecute : BT<AtomicCounter>
        {
            public SequenceParallelExecute(AtomicCounter counter, TestLatch latch)
            {
                StartWith(Sequence(
                    Parallel(ss => ss.AllSucceed(),
                        Execute(ctx => ctx.GlobalData.GetAndAdd(1)),
                        Execute(ctx => ctx.GlobalData.GetAndAdd(4))),
                    Execute(ctx => latch.CountDown())), counter);
            }
        }

        public class SequenceParallelReceive : BT<AtomicCounter>
        {
            public SequenceParallelReceive(AtomicCounter counter, TestLatch latch)
            {
                StartWith(Sequence(
                    Parallel(ss => ss.AllSucceed(),
                        ReceiveAny(Execute(ctx => ctx.GlobalData.GetAndAdd(1))),
                        ReceiveAny(Execute(ctx => ctx.GlobalData.GetAndAdd(4)))),
                    Execute(ctx => latch.CountDown())), counter);
            }
        }

        public class ParallelLoopReceive : BT<TestLatch>
        {
            private static Random _rand = new Random((int)DateTime.Now.Ticks);

            private Queue<string> _msgs1 = new Queue<string>();
            private Queue<string> _msgs2 = new Queue<string>();

            public ParallelLoopReceive(TestLatch latch)
            {
                StartWith(
                    Parallel(ss => ss.AllSucceed(),
                        Loop(ReceiveAny(Execute(Process1))),
                        Loop(ReceiveAny(Execute(Process2)))), latch);
            }

            public void Process1(TreeMachine.IContext ctx)
            {
                var str = ctx.CurrentMessage as string;
                if (str != null)
                {
                    _msgs1.Enqueue(str);
                    Self.Tell(new InternalMessage(), Sender);
                }
            }

            public void Process2(TreeMachine.IContext ctx)
            {
                var str = ctx.CurrentMessage as string;
                if (str != null)
                {
                    _msgs2.Enqueue(str);
                }

                var intmsg = ctx.CurrentMessage as InternalMessage;
                if (intmsg != null)
                {
                    Sender.Tell($"1{_msgs1.Dequeue()}2{_msgs2.Dequeue()}");
                }
            }

            public class InternalMessage { }
        }

        public class SequenceReceiveReverseThree : BT<TestLatch>
        {
            public SequenceReceiveReverseThree()
            {
                StartWith(
                    ReceiveAny(
                        Sequence(
                            ReceiveAny(
                                Sequence(
                                    ReceiveAny(Execute(ReplyEcho)),
                                    Execute(ReplyEcho))),
                            Execute(ReplyEcho))), null);
            }

            private void ReplyEcho(TreeMachine.IContext ctx) => Sender.Tell(ctx.CurrentMessage);
        }

        public class BecomePingPong : BT<List<string>>
        {
            private TestLatch _latch;

            public BecomePingPong(List<string> data, TestLatch latch)
            {
                _latch = latch;

                StartWith(ReceiveAny(o => (o as string) == "RUN", Become(Ping)), data);
            }

            private TreeMachine.IWorkflow Done() =>
                Condition(_ =>
                {
                    _latch.CountDown();
                    return _latch.IsOpen;
                });

            private TreeMachine.IWorkflow WithDoneAndDone(TreeMachine.IWorkflow wf) =>
                Sequence(
                    Selector(
                        Done(),
                        wf),
                    Execute(_ => Sender.Tell("DONE")));

            private TreeMachine.IWorkflow Ping() =>
                WithDoneAndDone(
                    Receive<string>(s => s == "PING",
                        Sequence(
                            Execute(ctx =>
                            {
                                ctx.GlobalData.Add("PING");
                                Self.Tell("PONG", Sender);
                            }),
                            Become(Pong))));

            private TreeMachine.IWorkflow Pong() =>
                WithDoneAndDone(
                    Receive<string>(s => s == "PONG",
                        Sequence(
                            Execute(ctx =>
                            {
                                ctx.GlobalData.Add("PONG");
                                Self.Tell("PING", Sender);
                            }),
                            Become(Ping))));
        }

        public class ParallelBecomer : BT<List<string>>
        {
            public ParallelBecomer()
            {
                StartWith(
                    Parallel(ss => ss.AllSucceed(),
                        Loop(Receive<string>(s => s == "THERE?", Execute(_ => Sender.Tell("HERE!")))),
                        Parallel(ss => ss.AllSucceed(),
                            Receive<string>(s => s == "KILL",
                                Become(() =>
                                    Sequence(
                                        Execute(_ => Sender.Tell("I TRIED")),
                                        Receive<string>(s => s == "THERE?", Execute(_ => Sender.Tell("I ATE HIM")))))))), null);
            }
        }

        public class SpawnConfirm : BT<object>
        {
            public SpawnConfirm()
            {
                StartWith(
                    Parallel(ss => ss.AllSucceed(),
                        Loop(Receive<string>(s => s.Equals("THERE?"), Execute(_ => Sender.Tell("HERE!")))),
                        Spawn(
                            Receive<string>(s => s.Equals("KILL"),
                                Become(() =>
                                    Sequence(
                                        Execute(_ => Sender.Tell("I TRIED")),
                                        Receive<string>(s => s == "THERE?", Execute(_ => Sender.Tell("I ATE HIM")))))))), null);
            }
        }

        public class LoopSpawn : BT<object>
        {
            public LoopSpawn()
            {
                StartWith(
                    Loop(
                        Spawn(
                            ReceiveAny(
                                Sequence(
                                    Execute(_ => Sender.Tell("KARU")),
                                    Become(() =>
                                        ReceiveAny(Execute(_ => Sender.Tell("SEL")))))))), null);
            }
        }

        public class AllConditions : BT<object>
        {
            public AllConditions()
            {
                StartWith(
                    ReceiveAny(
                        Selector(
                            Condition(x => x.CurrentMessage.Equals("NOT GO")),
                            Sequence(
                                Condition(x => x.CurrentMessage.Equals("GO")),
                                Execute(_ => Sender.Tell("I GO")),
                                ReceiveAny(
                                    Condition(x => x.CurrentMessage.Equals("LEFT"),
                                        Sequence(
                                            Execute(_ => Sender.Tell("I LEFT")),
                                            ReceiveAny(
                                                Condition(x => x.CurrentMessage.Equals("LEFT"),
                                                    @else: Sequence(
                                                        Execute(_ => Sender.Tell("I RIGHT")),
                                                        Loop(
                                                            ReceiveAny(
                                                                Condition(x => x.CurrentMessage.Equals("YES"),
                                                                    @then: Execute(_ => Sender.Tell("I YES")),
                                                                    @else: Execute(_ => Sender.Tell("I NO")))))))))))))), null);
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

        [Fact]
        public void BTActor_EmptyParallelOk()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new EmptyParallelOk(counter, latch)));

            latch.Ready(TimeSpan.FromDays(1));
            Assert.Equal(0, counter.Current);
        }

        [Fact]
        public void BTActor_SequenceParallelExecute()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceParallelExecute(counter, latch)));

            latch.Ready();
            Assert.Equal(5, counter.Current);
        }

        [Fact]
        public void BTActor_SequenceParallelReceive()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new SequenceParallelReceive(counter, latch)));

            bt.Tell(new object());
            bt.Tell(new object());

            latch.Ready(TimeSpan.FromSeconds(1));
            Assert.Equal(5, counter.Current);
        }

        [Fact]
        public void BTActor_ParallelLoopReceive()
        {
            var counter = new AtomicCounter(0);
            var latch = new TestLatch();

            var bt = Sys.ActorOf(Props.Create(() => new ParallelLoopReceive(latch)));

            bt.Tell("A", TestActor);
            bt.Tell("B", TestActor);
            ExpectMsg("1A2A");
            ExpectMsg("1B2B");

            bt.Tell("C");
            ExpectMsg("1C2C");
            bt.Tell("D");
            ExpectMsg("1D2D");
        }

        [Fact]
        public void BTActor_RecoverCurrentMessage()
        {
            var bt = Sys.ActorOf(Props.Create(() => new SequenceReceiveReverseThree()));

            foreach (var i in Enumerable.Range(1, 3))
            {
                bt.Tell(i.ToString(), TestActor);
            }

            foreach (var i in Enumerable.Range(1, 3).Reverse())
            {
                ExpectMsg(i.ToString());
            }
        }

        [Fact]
        public void BTActor_BecomePingPong()
        {
            var pipe = new List<string>();
            var latch = new TestLatch(5);

            var bt = Sys.ActorOf(Props.Create(() => new BecomePingPong(pipe, latch)));

            bt.Tell("RUN", TestActor);
            bt.Tell("PING", TestActor);

            latch.Ready();

            ExpectMsg("DONE");

            Assert.Equal(new[] { "PING", "PONG", "PING", "PONG" }, pipe);
        }

        [Fact]
        public void BTActor_Become_Is_Limited_By_Spawn_Scope()
        {
            var bt = Sys.ActorOf(Props.Create(() => new SpawnConfirm()));

            bt.Tell("THERE?", TestActor);
            ExpectMsg("HERE!");

            bt.Tell("KILL", TestActor);
            ExpectMsg("I TRIED");

            bt.Tell("THERE?", TestActor);

            ExpectMsgAllOf(3.Seconds(), "HERE!", "I ATE HIM");
        }

        [Fact]
        public void BTActor_Become_Crosses_NonSpawn_Scopes()
        {
            var bt = Sys.ActorOf(Props.Create(() => new ParallelBecomer()));

            bt.Tell("THERE?", TestActor);
            ExpectMsg("HERE!");

            bt.Tell("KILL", TestActor);
            ExpectMsg("I TRIED");

            bt.Tell("THERE?", TestActor);
            ExpectMsg("I ATE HIM");

            ExpectNoMsg(100);
        }

        [Fact]
        public void BTActor_Spawn_Resets_To_Original_Workflow()
        {
            var bt = Sys.ActorOf(Props.Create(() => new LoopSpawn()));

            bt.Tell(1, TestActor);
            bt.Tell(1, TestActor);
            bt.Tell(1, TestActor);
            bt.Tell(1, TestActor);

            ExpectMsg("KARU");
            ExpectMsg("SEL");
            ExpectMsg("KARU");
            ExpectMsg("SEL");
        }

        [Fact]
        public void BTActor_All_Conditions()
        {
            var bt = Sys.ActorOf(Props.Create(() => new AllConditions()));

            bt.Tell("GO", TestActor);
            bt.Tell("LEFT", TestActor);
            bt.Tell("RIGHT", TestActor);
            bt.Tell("YES");
            bt.Tell("NO");
            bt.Tell("NO");
            bt.Tell("YES");

            ExpectMsg("I GO");
            ExpectMsg("I LEFT");
            ExpectMsg("I RIGHT");
            bt.Tell("I YES");
            bt.Tell("I NO");
            bt.Tell("I NO");
            bt.Tell("I YES");
        }
    }
}
