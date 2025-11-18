namespace DemoMazeGame
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var x = 0;
            var y = 0;
            string userInput;

            while (true)
            {
                Console.WriteLine("You are in a maze. Which way do you want to go? N/S/E/W or Q to Quit");
                Console.WriteLine($"  You are at {x}, {y}");

                userInput = Console.ReadLine();

                Console.WriteLine("You entered: " + userInput);

                if (userInput.ToUpper() == "N")
                {
                    //y = y+1;
                    y++;
                }
                else if (userInput.ToUpper() == "E")
                {
                    x++;
                }
                else if (userInput.ToUpper() == "S")
                {
                    y--;
                }
                else if (userInput.ToUpper() == "W")
                {
                    x--;
                }
                else if (userInput.ToUpper() == "Q")
                {
                    break;
                }

                Console.WriteLine($"You are at {x}, {y}");
            } // end while

            Console.WriteLine("Thanks for playing!");
        }
    }
}
