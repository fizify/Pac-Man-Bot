﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PacManBot.Modules.PacManModule
{
    public class Game
    {
        static public List<Game> gameInstances = new List<Game>();

        public const string LeftEmoji = "⬅", UpEmoji = "⬆", DownEmoji = "⬇", RightEmoji = "➡", WaitEmoji = "⏸", RefreshEmoji = "🔃"; //Controls
        private const char PlayerChar = 'O', GhostChar = 'G', CornerChar = '_', PelletChar = '·', PowerPelletChar = '●'; //Read from map
        private const char PlayerDeadChar = 'X', GhostEatableChar = 'E'; //Displayed
        private readonly static Dir[] allDirs = { Dir.Up, Dir.Down, Dir.Left, Dir.Right };


        public ulong channelId;
        public ulong messageId = 1;
        public State state = State.Active;
        public int score = 0;
        public int timer = 0;
        private int pellets = 0;
        private char[,] board;
        private Player player;
        private List<Ghost> ghosts = new List<Ghost>();
        private Random random;


        public enum State { Active, Lose, Win }

        public enum AiType { Shadow, Speedy, Pokey, Bashful}

        public enum AiMode { Chase, Scatter, Eatable }

        public enum Dir { None, Up, Down, Left, Right }

        public class Pos
        {
            public int x, y;
            public Pos(int x, int y)
            {
                this.x = x;
                this.y = y;
            }

            public static bool operator !=(Pos pos1, Pos pos2) => !(pos1 == pos2);
            public static bool operator ==(Pos pos1, Pos pos2)
            {
                if (ReferenceEquals(pos1, null) || ReferenceEquals(pos2, null)) return ReferenceEquals(pos1, pos2);
                return pos1.x == pos2.x && pos1.y == pos2.y;
            }

            public static Pos operator +(Pos pos, Dir dir) //Moves position in the given direction
            {
                switch (dir)
                {
                    case Dir.Up:    return new Pos(pos.x, pos.y - 1);
                    case Dir.Down:  return new Pos(pos.x, pos.y + 1);
                    case Dir.Left:  return new Pos(pos.x - 1, pos.y);
                    case Dir.Right: return new Pos(pos.x + 1, pos.y);
                    default: return pos;
                }
            }

            public static float Distance(Pos pos1, Pos pos2) => (float)Math.Sqrt(Math.Pow(pos2.x - pos1.x, 2) + Math.Pow(pos2.y - pos1.y, 2));
        }

        private class Player
        {
            public Pos pos; //Position on the board
            public Dir dir = Dir.None; //Direction it's facing
            public int power = 0; //Time left of power mode

            public Player(Pos pos)
            {
                if (pos != null) this.pos = pos;
                else this.pos = new Pos(0, 0);
            }
        }

        private class Ghost
        {
            public Pos pos; //Position on the board
            public Pos target; //Tile it's trying to reach
            public Pos origin; //Tile it spawns in
            public Pos corner; //Preferred corner
            public Dir dir = Dir.None; //Direction it's facing
            public AiType type; //Ghost behavior type
            public AiMode mode = AiMode.Chase; //Ghost behavior mode
            public int pauseTime = 0; //Time remaining until it can move

            public readonly static char[] Appearance = { 'B', 'P', 'C', 'I' };

            public Ghost(Pos pos, AiType type, Pos corner)
            {
                this.pos = pos;
                this.type = type;
                origin = pos;
                this.corner = corner ?? origin;
                pauseTime = 6 + 6 * (int)type;
            }

            public void AI(Game game)
            {
                //Decide mode
                if (game.player.power > 0) mode = AiMode.Eatable;
                else
                {
                    if (game.timer % 60 > 45) mode = AiMode.Scatter; //In cycles of 60 ticks, the last 15 ticks will be in scatter mode
                    else mode = AiMode.Chase;
                }

                if (pauseTime > 0)
                {
                    pauseTime--;
                    return;
                }

                //Decide target
                switch (mode)
                {
                    case AiMode.Chase: //Normal
                        switch(type)
                        {
                            case AiType.Shadow:
                                target = game.player.pos;
                                break;

                            case AiType.Speedy:
                                target = game.player.pos + game.player.dir + game.player.dir; //Two squares ahead
                                break;

                            case AiType.Bashful:
                                target = game.player.pos + Opposite(game.player.dir) + Opposite(game.player.dir); //Two squares behind
                                break;

                            case AiType.Pokey:
                                if (Pos.Distance(pos, game.player.pos) > 8) target = game.player.pos;
                                else target = corner;
                                break;
                        }
                        break;

                    case AiMode.Scatter:
                        target = corner;
                        break;

                    case AiMode.Eatable:
                        for (int i = 0; i < 20; i++)
                        {
                            target = pos + (Dir)(game.random.Next(1, 5)); //Random adjacent empty space, 20 attempts
                            if (game.NonSolid(target)) break;
                        }
                        break;
                }

                //Track target
                Dir newDir = Dir.None;
                float distance = 100f;
                foreach (Dir testDir in allDirs) //Decides the direction that will get it closest to its target
                {
                    if (testDir == Opposite(dir) && mode != AiMode.Eatable) continue; //Can't turn 180º unless it's in eatable mode
                    if (game.NonSolid(pos + testDir) && Pos.Distance(pos + testDir, target) < distance)
                    {
                        distance = Pos.Distance(target, pos + testDir);
                        newDir = testDir;
                    }
                    //Console.WriteLine($"Target: {target.x},{target.y} / Ghost: {pos.x},{pos.y} / Test Dir: {(pos + testDir).x},{(pos + testDir).y} / Test Dist: {Pos.Distance(pos + testDir, target)}"); //For debugging AI
                }

                dir = newDir;
                pos += newDir;
                game.WrapAround(ref pos);
            }
        }


        public Game(ulong channelId)
        {
            this.channelId = channelId;
            random = new Random();

            GrabBoardFromFile();

            Pos playerPos = FindChar(PlayerChar); //Set player
            if (playerPos == null) playerPos = new Pos(0, 0);
            player = new Player(playerPos);
            board[playerPos.x, playerPos.y] = ' ';

            for (int i = 0; i < 4; i++) //Set ghosts
            {
                Pos ghostPos = FindChar(GhostChar);
                if (ghostPos == null) continue;
                Pos cornerPos = FindChar(CornerChar);
                ghosts.Add(new Ghost(ghostPos, (AiType)i, cornerPos));
                board[ghostPos.x, ghostPos.y] = ' ';
                board[cornerPos.x, cornerPos.y] = PelletChar;
            }
        }

        public void DoTick(Dir direction)
        {
            timer++;

            //Player
            if (direction != Dir.None) player.dir = direction;
            if (NonSolid(player.pos + direction)) player.pos += direction;
            WrapAround(ref player.pos);

            //Pellets
            if (player.power > 0) player.power--;

            if (board[player.pos.x, player.pos.y] == PelletChar)
            {
                pellets--;
                score += 10;
                board[player.pos.x, player.pos.y] = ' ';
            }
            else if (board[player.pos.x, player.pos.y] == PowerPelletChar)
            {
                pellets--;
                player.power += 15;
                score += 50;
                board[player.pos.x, player.pos.y] = ' ';
            }

            if (pellets == 0) state = State.Win;

            //Ghosts
            foreach (Ghost ghost in ghosts)
            {
                bool didAI = false;
                while (true) //Checks player collision before and after AI
                {
                    if (player.pos == ghost.pos)
                    {
                        if (player.power > 0)
                        {
                            ghost.pos = ghost.origin;
                            ghost.pauseTime = 6;
                            score += 200;
                        }
                        else state = State.Lose;

                        didAI = true; //Skips AI
                    }

                    if (didAI) break;

                    ghost.AI(this);
                    didAI = true;
                }
            }
        }

        public string Display
        {
            get
            {
                StringBuilder boardString = new StringBuilder(); //The final display
                char[,] displayBoard = (char[,])board.Clone(); //The temporary display array

                //Adds ghost and player
                foreach (Ghost ghost in ghosts) displayBoard[ghost.pos.x, ghost.pos.y] = (ghost.mode == AiMode.Eatable) ? GhostEatableChar : Ghost.Appearance[(int)ghost.type];
                displayBoard[player.pos.x, player.pos.y] = (state == State.Lose) ? PlayerDeadChar : PlayerChar;

                //Converts 2d array to string
                for (int y = 0; y < displayBoard.GetLength(1); y++)
                {
                    for (int x = 0; x < displayBoard.GetLength(0); x++)
                    {
                        boardString.Append(displayBoard[x, y]);
                    }
                    boardString.Append('\n');
                }

                //Add text to the side
                string[] info = {
                    $" │ #Time: {timer}\n",
                    $" │ #Score: {score}\n",
                    $" │ #Power: {player.power}\n",
                    $" │\n",
                    $" │ {Ghost.Appearance[0]} - \"Blinky\" ({(AiType)0})\n",
                    $" │ {Ghost.Appearance[1]} - \"Pinky\"  ({(AiType)1})\n",
                    $" │ {Ghost.Appearance[3]} - \"Inky\"   ({(AiType)3})\n",
                    $" │ {Ghost.Appearance[2]} - \"Clyde\"  ({(AiType)2})\n"
                };
                for (int i = 0; i < info.Length; i++)
                {
                    int startIndex = 1 + i * displayBoard.GetLength(0);
                    for (int j = i; j >= 0; j--) startIndex += info[j].Length;
                    boardString.Replace("\n", info[i], startIndex, displayBoard.GetLength(0));
                }

                //Code tags
                switch (state)
                {
                    case State.Active:
                        boardString.Insert(0, "```css\n");
                        break;

                    case State.Lose:
                        boardString.Insert(0, "```diff\n");
                        boardString.Replace("\n", "\n-"); //All red
                        break;

                    case State.Win:
                        boardString.Insert(0, "```diff\n");
                        boardString.Replace("\n", "\n+"); //All green
                        break;
                }
                boardString.Append("\n```");


                return boardString.ToString();
            }
        }

        private Pos FindChar(char c, int index = 0)
        {
            for (int y = 0; y < board.GetLength(1); y++)
            {
                for (int x = 0; x < board.GetLength(0); x++)
                {
                    if (board[x, y] == c)
                    {
                        if (index > 0) index--;
                        else
                        {
                            return new Pos(x, y);
                        }
                    }
                }
            }

            return null;
        }

        private bool NonSolid(int x, int y, bool collideGhosts = false) => NonSolid(new Pos(x, y), collideGhosts);
        private bool NonSolid(Pos pos, bool collideGhosts = false)
        {
            WrapAround(ref pos);

            if (collideGhosts)
            {
                foreach (Ghost ghost in ghosts)
                {
                    if (ghost.pos == pos) return false;
                }
            }

            return (board[pos.x, pos.y] == ' ' || board[pos.x, pos.y] == PelletChar || board[pos.x, pos.y] == PowerPelletChar);
        }

        private void WrapAround(ref Pos pos)
        {
            if      (pos.x < 0) pos.x = board.GetLength(0) + pos.x;
            else if (pos.x > board.GetLength(0) - 1) pos.x -= board.GetLength(0);
            else if (pos.y < 0) pos.y = board.GetLength(1) + pos.y;
            else if (pos.y > board.GetLength(1) - 1) pos.y -= board.GetLength(1);
        }

        private void GrabBoardFromFile(string file = "board.txt")
        {
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            int width = lines[0].Length;
            int height = lines.Length;

            char[,] board = new char[width, height];
            try
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        board[x, y] = lines[y].ToCharArray()[x];
                        if (board[x, y] == PelletChar || board[x, y] == PowerPelletChar) pellets++;
                    }
                }
            }
            catch { throw new Exception("Invalid board"); }

            this.board = board;
        }


        private static Dir Opposite(Dir dir)
        {
            switch (dir)
            {
                case Dir.Up: return Dir.Down;
                case Dir.Down: return Dir.Up;
                case Dir.Left: return Dir.Right;
                case Dir.Right: return Dir.Left;
                default: return Dir.None;
            }
        }
    }
}