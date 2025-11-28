DRL COURSE BASED APPROACH

problem definition
The problem is how to find fastest way to build "barracks" in rts game. Barracks are important because if you have barracks before your opponent you got the change of first attack. This change is important.
So base goal is find the fastest way to build barracks trough workers gathering and building capabilities.

Estimated strategy
Recurit more worker for fast gathering is the estimated strategy from algorithm. Expectation is shaped around the more-worker/current-resource balance

states explained(64)
there ara 64 states.
4 is wood level ([0-250],[250-500],[500-750],[1000+])
4 is stone level ([0-250],[250-500],[500-750],[1000+])
2 is meat level ([0-50],[50+])
2 is population level (popMax(1),popIsNotMax(0))

action spaces(5)
Collect wood
Collect stone
Collect meat
Recurit worker
Build barrack


rewards
step based time punishment = -0.1f
collect wood succesfully = 5f
collect stone succesfully = 5f
collect meat succesfully = 0f // for dont spend to much time on collecting meat without  recurit worker
recurit worker succesfully = 50f
Build Barrack succesfully (WIN CONDITION) = 1000f+ speedBonus


Project sructure
Project Sturcture is constructed based on discrete sturctures. 
Main discrimination is data and visual structure. 
Game is complately playable with only data. So game does not need actualy visuals frames etc.

Strong aspects
Projects storng aspects came for its own structure. 
Thanks to discrete structure similations can be complate vithout visuals. 
That meanas very large amount of speed when conducting simulation.

how to see and how to test
Clone repo
git clone https://github.com/MuhammetAliKaya/RTS-AI.git
Add clonned folder in unity hub
open folder with unity(6000.0.22)
find Assets => Scenes => SimV2 (Double Click) and open scene
in this step you see blue scren and some uı elements

In hierarchy window find ExperimentManager object and click
check Inspector window for rl parameters map size almost all variables about game and RL.
You can change variables and pres play button and you can watch the rl edication
if you want to watch trained model check the Run Inference Mode but you want to train first do not check Run Inference Mode
Aditionally there some important features
if you want to süper fast mode(thanks to data sriven structure) you can check Run Fast option and specify Eposides per frame.(Up to your processorsor performance)
but you want to see exatly what happen then training so do not check run fast. But you think that is to slow ı want to watch but faster so you can change visual simulation speed

what is next
Now having a good model for fastest barrak build.What we do with it. 
Main usage is probably beeing on of enemy ai's strategy. 
More developed ai can chose this as a function or desicion maker in certain cases.
For example if there is a attacker ai, it uses out qtable for make desicion in first phase of game. When ai get barracks came back to its own strategy.
 