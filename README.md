# RoboLink

A command line C# application to recursively create NTFS hard links, and additionally merge and flatten multiple source directories.

This will be useful if you need to copy large files (or large numbers of files) from one directory to another without the intention of modifying them.

Inspired by [Microsoft RoboCopy](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy), which does not have the above features.

Information about [Hard Links and Junctions](https://msdn.microsoft.com/en-us/library/windows/desktop/aa365006.aspx)

## Usage

	robolink.exe source1 {source2...} dest [options]
	  /R - recurse into subdirectories of sources and destination
	  /P - purge files from destination missing or newer in sources
	  /F - flatten subdirectories of sources
	  /C - commit changes
	  /Q - ignore duplicate source files
	  /IF pattern - include files matching pattern
	  /ID pattern - include directories matching pattern
	  /XF pattern - exclude files matching pattern
	  /XD pattern - exclude directories matching pattern
	  /? - usage information and exit

## Example

Synchronise D:\src with D:\dest 

	robolink.exe D:\src D:\dest /c /r /p

## Download

[EXE file](https://www.dropbox.com/s/cw8982ldb274s2k/robolink.exe?dl=0)
