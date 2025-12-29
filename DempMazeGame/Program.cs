namespace DemoMazeGame
{
    internal class Program
    {// arrays are collections of data, identified by brackets after the var type
        static int[,] map =  // x, y mapping = x is horizontal axis, which is col. y is vertical axis, which is row
        {
            {1,1,1,1,1,1,1,1,1,1},
            {1,0,0,0,0,0,0,0,0,1},
            {1,0,0,0,0,0,0,0,0,1},
            {1,0,0,0,0,0,0,0,0,1},
            {1,0,0,0,0,0,0,0,2,1},
            {1,1,1,1,1,1,1,1,1,1},
        };

        static void Main(string[] args)
        {
            var row = 1;
            var col = 1; 
            string userInput;

            while (true)
            {
                Console.WriteLine("You are in a maze. Which way do you want to go? N/S/E/W or Q to Quit");
                Console.WriteLine($"  You are at {col}, {row}");

                userInput = Console.ReadLine();

                Console.WriteLine("You entered: " + userInput);

                if (userInput.ToUpper() == "N")
                {
                    if (IsValidMove(row, col, "N"))
                    {
                        row--;
                    }
                    else
                    {
                        Console.WriteLine("You can't go that way!");
                    }
                }
                else if (userInput.ToUpper() == "E")
                {
                    if (IsValidMove(row, col, "E"))
                    {
                        col++;
                    }
                    else
                    {
                        Console.WriteLine("You can't go that way!");
                    }
                }
                else if (userInput.ToUpper() == "S")
                {
                    if (IsValidMove(row, col, "S"))
                    {
                        row++;
                    }
                    else
                    {
                        Console.WriteLine("You can't go that way!");
                    }
                }
                else if (userInput.ToUpper() == "W")
                {
                    if (IsValidMove(row, col, "W"))
                    {
                        col--;
                    }
                    else
                    {
                        Console.WriteLine("You can't go that way!");
                    }
                }
                else if (userInput.ToUpper() == "Q")
                {
                    break;
                }

                if (map[row, col] == 2)
                {
                    Console.WriteLine("You found the exit! Contratulations!");
                    break;
                }

                Console.WriteLine($"You are at {col}, {row}");
            } // end while

            Console.WriteLine("Thanks for playing!");
        }

        // private - tells the code who can use this method (private = only this class, public = anyone, internal = this project)
        // static - optional modifier, tells c# that the method can be called without creating an instance of the class
        // bool - return type, tells c# what type of data the method will return (void = no return, int, string, bool, etc.)
        // IsValidMove - name of the method
        // parens - parameters, passes data into the method
        private static bool IsValidMove(int row, int col, string direction)
        {
            // get desired location
            int newCol = col;
            int newRow = row;

            if (direction.ToUpper() == "N")
            {
                newRow = row-1;
            }
            else if (direction.ToUpper() == "S")
            {
                newRow = row+1;
            }
            else if (direction.ToUpper() == "W")
            {
                newCol = col-1;
            }
            else if (direction.ToUpper() == "E")
            {
                newCol = col+1;
            }

            if (map[newRow, newCol] == 1)
            {
                return false;
            }

            return true;
        }
    }
}


    

