# AnyRadianceLoader
A mod that allows you to load any of the three available AnyRadiance versions without manually replacing mod files. You only need to press a single button in the menu.

To select the desired AnyRadiance version, open the mod menu and choose it.

IMPORTANT:
DO THIS ONLY FROM THE MAIN MENU.
If you attempt to do this outside the main menu, the mod will not perform any action.

This protection is intentional. When a save file is loaded, all assets have already been initialized, and loading the mod from that state would be incorrect and could lead to errors.

After selecting a version, the mod will unload the current files and immediately initialize the files for the chosen AnyRadiance version.

Once a version has been selected, it will not be possible to choose another version during the same game session. This is intentional to prevent version conflicts (for example, installing AnyRadiance 1.0 and then installing AnyRadiance 2.0 on top of it, which would break the internal logic).

To select a different AnyRadiance version, simply restart the game, then open the mod settings again and choose the desired version.

When you exit the game, the mod automatically removes the AnyRadiance files that were installed.
