using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test
{
    public static class Positions
    {
        public static PerftPosition[] Perfs = [
            new() {
                Title = "standard chess position",
                FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                P1 = 20,
                P2 = 400,
                P3 = 8902,
                P4 = 197281,
                P5 = 4865609,
                P6 = 119060324,
                P7 = 3195901860
            },
            new(){
                Title = "pawns",
                FEN = "4k3/pppppppp/8/8/8/8/PPPPPPPP/3K4 w - - 0 1",
                P1 = 18,
                P2 = 324,
                P3 = 5658,
                P4 = 98766,
                P5 = 1683599,
                P6 = 28677559,
                P7 = 479771205
            },
            new(){
                Title = "queens",
                FEN = "3k1q2/8/8/8/8/8/8/2Q1K3 w - - 0 1",
                P1 = 20,
                P2 = 322,
                P3 = 6371,
                P4 = 123074,
                P5 = 2456875,
                P6 = 48349901,
                P7 = 961477665
            },
            new(){
                Title = "bishops",
                FEN = "2bk1b2/8/8/8/8/8/8/2B1KB2 w - - 0 1",
                P1 = 18,
                P2 = 305,
                P3 = 5587,
                P4 = 100301,
                P5 = 1889516,
                P6 = 35099794,
                P7 = 673156899
            },
            new(){
                Title = "rooks",
                FEN = "2bk1b2/8/8/8/8/8/8/2B1KB2 w - - 0 1",
                P1 = 26,
                P2 = 568,
                P3 = 13744,
                P4 = 314346,
                P5 = 7594526,
                P6 = 179862938,
                P7 = 4408318687
            },
            new(){
                Title = "kings",
                FEN = "3k4/8/8/8/8/8/8/4K3 w - - 0 1",
                P1 = 5,
                P2 = 25,
                P3 = 170,
                P4 = 1156,
                P5 = 7922,
                P6 = 53932,
                P7 = 375660
            },
            new(){
                Title = "knights",
                FEN = "1n1k2n1/8/8/8/8/8/8/1N2K1N1 w - - 0 1",
                P1 = 11,
                P2 = 121,
                P3 = 1551,
                P4 = 19764,
                P5 = 273291,
                P6 = 3736172,
                P7 = 54351347
            },
            new(){
                Title = "enpassant pawns",
                FEN = "8/4pppp/2k5/pppp4/4PPPP/5K2/PPPP4/8 w - - 0 1",
                P1 = 18,
                P2 = 308,
                P3 = 5353,
                P4 = 91461,
                P5 = 1561105,
                P6 = 26214838,
                P7 = 435307144
            },
            new(){
                Title = "bishops pawns and rooks",
                FEN = "3kr3/7p/8/b2p2P1/1p2P2B/8/P7/3RK3 w - - 0 1",
                P1 = 18,
                P2 = 308,
                P3 = 5353,
                P4 = 91461,
                P5 = 1561105,
                P6 = 26214838,
                P7 = 435307144
            },
            new(){
                Title = "bishops pawns rooks knights queens",
                FEN = "3kr3/q6p/8/b2pn1P1/1p1NP2B/8/P6Q/3RK3 w - - 0 1",
                P1 = 35,
                P2 = 1134,
                P3 = 36189,
                P4 = 1161095,
                P5 = 37473046,
                P6 = 1214202944,
                P7 = 39761457145
            },
            new(){
                Title = "pawn promotion",
                FEN = "r2k4/1P6/P1PP4/8/8/4pp1p/6p1/4K2R w - - 0 1",
                P1 = 16,
                P2 = 156,
                P3 = 1451,
                P4 = 14421,
                P5 = 157920,
                P6 = 1862695,
                P7 = 24837114
            },
            new(){
                Title = "pawn knight king bishop queen rook enpassant promotion",
                FEN = "r2k3r/1pq5/2b5/6Np/Pn6/5B2/5QP1/R3K2R w KQkq - 0 1",
                P1 = 46,
                P2 = 1835,
                P3 = 75154,
                P4 = 2993847,
                P5 = 120745301,
                P6 = 4808030694,
                P7 = 192968428152
            },
            new(){
                Title = "flipped standard",
                FEN = "RNBKQBNR/PPPPPPPP/8/8/8/8/pppppppp/rnbqkbnr w - - 0 1",
                P1 = 4,
                P2 = 16,
                P3 = 176,
                P4 = 1936,
                P5 = 22428,
                P6 = 255135,
                P7 = 3830756
            },
            new(){
                Title = "major pieces",
                FEN = "rnbk1bnr/3q4/8/8/8/8/3Q4/RNBK1BNR w KQkq - 0 1",
                P1 = 36,
                P2 = 1190,
                P3 = 43693,
                P4 = 1558933,
                P5 = 60772455,
                P6 = 2349024280,
                P7 = 96982521235
            },
        ];
    }

    public class PerftPosition
    {
        public string Title { get; set; } = default!;
        public string FEN { get; set; } = default!;
        public long P1 { get; set; }
        public long P2 { get; set; }
        public long P3 { get; set; }
        public long P4 { get; set; }
        public long P5 { get; set; }
        public long P6 { get; set; }
        public long P7 { get; set; }
    }

}
