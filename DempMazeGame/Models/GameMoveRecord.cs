namespace DemoMazeGame.Models
{
    // Simple record of a move for tracking history during gameplay
    // Used by BuildPrompt to show the AI its move history
    public class GameMoveRecord
    {
        public int MoveNumber { get; set; }
        public string Direction { get; set; } = "";
        public int FromRow { get; set; }
        public int FromCol { get; set; }
        public int ToRow { get; set; }
        public int ToCol { get; set; }
        public bool HitWall { get; set; }

        // Format as compact string like "1. N: (1,1)→(1,0)" or "1. N: (1,1)→WALL"
        public string ToCompactString()
        {
            if (HitWall)
            {
                return $"{MoveNumber}. {Direction}: ({FromCol},{FromRow})→WALL";
            }
            else
            {
                return $"{MoveNumber}. {Direction}: ({FromCol},{FromRow})→({ToCol},{ToRow})";
            }
        }
    }
}

