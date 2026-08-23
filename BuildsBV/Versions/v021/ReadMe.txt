TV Movies Channel Guide
=======================

This app displays a TV-style channel guide and plays your movies through
an embedded VLC player.

Folder layout
-------------
TV GUIDE\
  Channel 1\Schedual\
  Channel 2\Schedual\
  ...
  Channel 15\Schedual\

Each "Channel N" folder is one channel. Drop .mp4 files either directly in
the channel folder or in its "Schedual" subfolder (or both) - the app reads
both locations and schedules them into a repeating 24-hour daily lineup.

LinkToRoot.txt
--------------
Contains the file path the app should point at for your movie library root
(the "TV GUIDE" folder). Edit this file if you move your library to a
different location.

These two files, and this empty "TV GUIDE" folder tree, are copied/created
next to the app automatically on every build and publish.
