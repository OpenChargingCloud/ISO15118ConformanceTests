using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.Tests.Timing;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// Proves the SECC sequence timeout using <see cref="ManualTimeProvider"/> — no real waiting, the
    /// clock is advanced programmatically between two <c>Handle</c> calls.
    /// </summary>
    [TestFixture]
    public class TimeoutTests
    {
        private static V2G_Message Wrap(byte[] sid, BodyBaseType body) =>
            new(new MessageHeaderType(sid, Notification: null, Signature: null), new BodyType(body));

        [Test]
        public void SequenceTimeout_ThrowsWhenExceeded()
        {
            var clock = new ManualTimeProvider();
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), clock);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            clock.Advance(TimeSpan.FromSeconds(61));

            Assert.That(() => secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null))),
                Throws.InstanceOf<SessionAborted>().With.Message.Contain("sequence timeout"));
        }

        [Test]
        public void SequenceTimeout_DoesNotThrowWhenWithinBudget()
        {
            var clock = new ManualTimeProvider();
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), clock);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            clock.Advance(TimeSpan.FromSeconds(30));

            Assert.That(() => secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null))), Throws.Nothing);
        }
    }
}
