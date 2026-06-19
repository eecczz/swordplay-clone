Hello. Thank you for buying this asset!

QUICK START
Asset contains two ready scripts that you can use for your project:
- "TargetMatching" (Matching to another object's placement);
- "Placement Matching" (Matching to a specific position and rotation).
Open an example scene to see how it works.

Also there is "RigidbodyMatchingBase" abstract class where main calculations are made.
You can create a class inherited from "RigidbodyMatchingBase" and implement custom input logics.
E.g. physical player movements, spaceship etc.

About position and rotation drives.
You need to try different values to spring and damper. Because matching stability depends on it.
Stable values for rigidbody with 1kg mass:
- Spring 1000;
- Damper 50.

I wish you success!