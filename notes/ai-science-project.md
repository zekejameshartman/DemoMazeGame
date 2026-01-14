AI Spatial Navigation Test

Question: Can AI models navigate a maze using only spatial text data without visual cues?

Research Problem: I've been interested in learning to code, and I've heard a lot about AI lately. I wanted to choose a project to learn coding, and thought that this would be agood way to explore both coding and AI. I knew that were different AI "models" and wanted to see how they performed against each other. I chose a maze program as the benchmar because it seemed like a good way to test an AI's spatial reasoning. 

Hypothesis: I predict that larger more expensive AI models will perform better at maze navigation than smaller, cheaper models on average. 

Research Plan:
- Build a maze game program using C#
- Build AI integration for AI to play the game by itself
- Run tests with different AI models
- Analyze and summarize the results

Journal:
Phase 1: My dad guided me through making a new C# project. We created a working program for a human player to move around an empty grid. I learned how to make simple text prompts on the console, and using player input to move the player around. I learned how to use git and GitHub to store my code and work together with my dad on it.

Phase 2: I created a wall system and map using 2D arrays. I also created an exit so that the game would have a win condition. I created the map using a series of arrays with 1s and 0s, where 0s are open space and 1s are walls. Here is an exmple of the map written in code:
private int[,] map =
{
    {1,1,1,1,1,1,1,1,1,1,1,1},
    {1,0,0,0,1,0,0,0,0,0,0,1},
    {1,0,1,0,1,0,1,1,1,1,0,1},
    {1,0,1,0,0,0,0,0,0,1,0,1},
    {1,0,1,1,1,1,1,1,0,1,0,1},
    {1,0,0,0,0,0,0,0,0,1,0,1},
    {1,1,1,1,1,1,1,1,0,1,0,1},
    {1,0,0,0,0,0,0,0,0,1,0,1},
    {1,0,1,1,1,1,1,1,1,1,0,1},
    {1,0,0,0,0,0,0,0,0,0,0,2},
    {1,1,1,1,1,1,1,1,1,1,1,1},
};

With this the main game was completed. We just needed to integrate AI into it.

Phase 3: The code for the AI was too complex for a beginner, so my dad wired up an AI player to my game. We ran some simple tests with the AI, and observed it's behavior. Our first versions, it did not perform very well. We were giving it very limited information, and not allowing the AI to use reasoning. The AI player, no matter how smart the model, would usually just move back and forth in the same area.

Phase 4: My dad helped a lot during this phase because we realized we needed to give the AI more information. We started adding details to it's prompt including the ability to see how far in each direction it could move, a full history of it's past moves, and we figured out how to tell the API to allow the AI to use reasoning, meaning the AI could 'think' beore answering. We also added settings to change what we would tell the AI. For example, we could now tell the AI what grid position it's in plus what grid position the exit node is in. We found the AI started doing better, but we discovered something suprising. When we tell the AI where the exit is, it actually does worse! It gets fixated on trying to move in the direction the exit is in, but our map includes a tricky path in the middle that requies going away from the exit. The AI is more focused in going towards the exit than it is in finding a valid path.

Phase 5: We needed to record and get more data, we created code to log everything so that we can analyze it afterward.

