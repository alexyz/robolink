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

	robolink.exe /c /r /p D:\src D:\dest

Collect client or shared jar files into a single directory excluding those ending in -server.jar.
This is useful for launching the [NextGen Connect](https://www.nextgen.com/products-and-services/integration-engine) client from the command line.

	robolink.exe /c /p /r /f /q /if *.jar /xf *-server.jar client-lib extensions %temp%\mirth

## Download

See GitHub releases!

You will probably get a warning from SmartScreen that the program is untrusted.

You can either trust the program, or read the source and compile it yourself.
