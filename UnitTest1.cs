using Xunit;

namespace ChordDemo.Tests
{
    public class ChordTests
    {
        [Fact]
        public void Test_HashToId_Deterministic()
        {
            int id1 = ChordRing.HashToId("apple", 8);
            int id2 = ChordRing.HashToId("apple", 8);
            Assert.Equal(id1, id2); // Cung mot key -> cung mot ID
        }

        [Fact]
        public void Test_NodeJoin_SuccessorNotNull()
        {
            var ring = new ChordRing(8);
            var n1 = ring.CreateNode("192.168.0.1:5000"); n1.Create();
            var n2 = ring.CreateNode("192.168.0.2:5000"); n2.Join(n1);

            Assert.NotNull(n2.Successor);
        }

        [Fact]
        public void Test_PutAndGet_WorkCorrectly()
        {
            var ring = new ChordRing(8);
            var n1 = ring.CreateNode("192.168.0.1:5000"); n1.Create();
            var n2 = ring.CreateNode("192.168.0.2:5000"); n2.Join(n1);

            // on dinh vong
            for (int i = 0; i < 10; i++)
                foreach (var n in ring.Nodes) { n.Stabilize(); n.FixFingers(); }

            string key = "apple";
            int id = ChordRing.HashToId(key, 8);

            var owner = n1.FindSuccessor(id);
            owner.Store[key] = "🍎";

            var found = n2.FindSuccessor(id);
            string value = found.Store[key];

            Assert.Equal("🍎", value);
        }
    }
}
