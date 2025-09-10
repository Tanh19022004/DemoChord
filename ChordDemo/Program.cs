// ChordDemo.cs
// .NET 6+ :  dotnet new console -n ChordDemo && replace Program.cs with this file's content.
// Build & run: dotnet run
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ChordDemo
{
    class Program
    {
        static void Main()
        {
            // Tham số Chord
            int m = 8; // số bit ID (2^m node tối đa trong mô phỏng)
            var ring = new ChordRing(m);

            // Tạo node đầu tiên
            var n1 = ring.CreateNode("192.168.0.1:5000");
            n1.Create(); // khởi tạo vòng

            // Thêm thêm node
            var n2 = ring.CreateNode("192.168.0.2:5000"); n2.Join(n1);
            var n3 = ring.CreateNode("192.168.0.3:5000"); n3.Join(n1);
            var n4 = ring.CreateNode("192.168.0.4:5000"); n4.Join(n1);

            // Ổn định vòng vài vòng lặp
            for (int i = 0; i < 20; i++)
            {
                foreach (var n in ring.Nodes) { n.Stabilize(); n.FixFingers(); n.CheckPredecessor(); }
            }

            Console.WriteLine("=== Trang thai vong sau khi on dinh ===");
            foreach (var n in ring.Nodes.OrderBy(x => x.Id))
            {
                Console.WriteLine(n.ToDebugString());
            }

            // Put/Get dữ liệu
            Console.WriteLine("\n=== Thao tac Put/Get ===");
            var kvs = new (string key, string value)[]
            {
                ("apple", "🍎"),
                ("banana", "🍌"),
                ("chord", "DHT"),
                ("distributed", "systems"),
                ("chatgpt", "assistant")
            };

            foreach (var (k, v) in kvs)
            {
                var owner = n1.FindSuccessor(ChordRing.HashToId(k, m));
                owner.Store[k] = v;
                Console.WriteLine($"Put: key=\"{k}\" (id={ChordRing.HashToId(k, m)}) -> node {owner}");
            }

            // Tra cứu từ các node khác nhau
            foreach (var (k, _) in kvs)
            {
                var askers = new[] { n2, n3, n4 };
                foreach (var asker in askers)
                {
                    var node = asker.FindSuccessor(ChordRing.HashToId(k, m));
                    string value = node.Store.TryGetValue(k, out var vv) ? vv : "<missing>";
                    Console.WriteLine($"Get: \"{k}\" tu {asker} -> owner {node} -> value: {value}");
                }
            }

            // Thêm một node nữa rồi ổn định
            Console.WriteLine("\n=== Them node moi va tai on dinh ===");
            var n5 = ring.CreateNode("192.168.0.5:5000"); n5.Join(n3);
            for (int i = 0; i < 20; i++)
            {
                foreach (var n in ring.Nodes) { n.Stabilize(); n.FixFingers(); n.CheckPredecessor(); }
            }

            foreach (var n in ring.Nodes.OrderBy(x => x.Id))
            {
                Console.WriteLine(n.ToDebugString());
            }

            Console.WriteLine("\nDone.");
        }
    }

    /// <summary>
    /// Mô phỏng “mạng” để tạo node và băm ID.
    /// </summary>
    public class ChordRing
    {
        public int M { get; }
        public int Mod => 1 << M; // 2^m
        public List<ChordNode> Nodes { get; } = new();

        public ChordRing(int m) { M = m; }

        public ChordNode CreateNode(string address)
        {
            int id = HashToId(address, M);
            var node = new ChordNode(this, id, address);
            Nodes.Add(node);
            return node;
        }

        public static int HashToId(string s, int m)
        {
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            // Lấy m bit đầu tiên từ SHA1 (big endian)
            int bits = 0;
            int need = m;
            int idx = 0;
            while (need > 0)
            {
                int take = Math.Min(8, need);
                bits <<= take;
                bits |= (hash[idx] >> (8 - take)) & ((1 << take) - 1);
                need -= take;
                idx++;
            }
            return bits & ((1 << m) - 1);
        }

        // Hàm tiện ích kiểm tra x có nằm trong (a, b] hay [a, b) trên vòng modulo 2^m
        public bool InRange(int x, int a, int b, bool leftOpen = true, bool rightClosed = true)
        {
            int mod = Mod;
            a = ((a % mod) + mod) % mod;
            b = ((b % mod) + mod) % mod;
            x = ((x % mod) + mod) % mod;

            if (a < b)
            {
                if (leftOpen && rightClosed) return x > a && x <= b;
                if (!leftOpen && rightClosed) return x >= a && x <= b;
                if (leftOpen && !rightClosed) return x > a && x < b;
                return x >= a && x < b; // !leftOpen && !rightClosed
            }
            else if (a > b)
            {
                // wrap-around
                bool left = leftOpen ? x > a : x >= a;
                bool right = rightClosed ? x <= b : x < b;
                return left || right;
            }
            else
            {
                // a == b: toàn bộ vòng trừ/bao gồm tuỳ mở/đóng
                if (leftOpen && rightClosed) return x != a;
                if (!leftOpen && rightClosed) return true;      // [a, a] -> mọi x
                if (leftOpen && !rightClosed) return x != a;
                return true; // [a,a) coi như mọi x (mô phỏng)
            }
        }
    }

    public class FingerEntry
    {
        public int Start;         // (n + 2^{k-1}) mod 2^m
        public ChordNode Node;    // node chịu trách nhiệm cho Start
        public override string ToString() => $"{Start}->{Node?.Id}";
    }

    public class ChordNode
    {
        public readonly ChordRing Ring;
        public int Id { get; }
        public string Address { get; }
        public ChordNode Successor { get; private set; }
        public ChordNode Predecessor { get; private set; }
        public FingerEntry[] Fingers { get; }
        public Dictionary<string, string> Store { get; } = new();

        public ChordNode(ChordRing ring, int id, string address)
        {
            Ring = ring; Id = id; Address = address;
            Fingers = new FingerEntry[ring.M];
            for (int i = 0; i < ring.M; i++)
            {
                Fingers[i] = new FingerEntry
                {
                    Start = (Id + (1 << i)) & (ring.Mod - 1),
                    Node = null
                };
            }
        }

        public void Create()
        {
            Successor = this;
            Predecessor = null;
            for (int i = 0; i < Ring.M; i++) Fingers[i].Node = this;
        }

        public void Join(ChordNode known)
        {
            if (known == null) { Create(); return; }
            Predecessor = null;
            Successor = known.FindSuccessor(Id);
            // Finger[0] hay successor đều trỏ cùng node
            Fingers[0].Node = Successor;
        }

        public ChordNode FindSuccessor(int id)
        {
            var n0 = FindPredecessor(id);
            return n0.Successor;
        }

        public ChordNode FindPredecessor(int id)
        {
            var n = this;
            while (!Ring.InRange(id, n.Id, n.Successor.Id, leftOpen: true, rightClosed: true))
            {
                var next = n.ClosestPrecedingFinger(id);
                if (next == n) break; // tránh vòng lặp nếu finger kém
                n = next;
            }
            return n;
        }

        public ChordNode ClosestPrecedingFinger(int id)
        {
            for (int i = Ring.M - 1; i >= 0; i--)
            {
                var f = Fingers[i].Node;
                if (f != null && Ring.InRange(f.Id, this.Id, id, leftOpen: true, rightClosed: false))
                    return f;
            }
            return this;
        }

        public void Stabilize()
        {
            var x = Successor?.Predecessor;
            if (x != null && Ring.InRange(x.Id, this.Id, Successor.Id, leftOpen: true, rightClosed: false))
            {
                Successor = x;
                Fingers[0].Node = Successor;
            }
            Successor?.Notify(this);
        }

        public void Notify(ChordNode n)
        {
            if (Predecessor == null || Ring.InRange(n.Id, Predecessor.Id, this.Id, leftOpen: true, rightClosed: false))
                Predecessor = n;
        }

        private int _fixNext = 1; // đã set finger[0] = successor trong Join
        public void FixFingers()
        {
            // lần lượt cập nhật finger entries
            int i = _fixNext;
            _fixNext = (_fixNext + 1) % Ring.M;
            int start = Fingers[i].Start;
            Fingers[i].Node = FindSuccessor(start);
        }

        public void CheckPredecessor()
        {
            // Trong mô phỏng không có node chết; để trống.
            // Thực tế: nếu predecessor không phản hồi thì đặt null.
        }

        public override string ToString() => $"[{Id}]@{Address}";

        public string ToDebugString()
        {
            var succ = Successor != null ? Successor.Id.ToString() : "null";
            var pred = Predecessor != null ? Predecessor.Id.ToString() : "null";
            var fingers = string.Join(", ", Fingers.Select((f, i) => $"{i}:{f.Node?.Id}"));
            return $"Node {this}: pred={pred}, succ={succ}, fingers=({fingers})";
        }
    }
}
