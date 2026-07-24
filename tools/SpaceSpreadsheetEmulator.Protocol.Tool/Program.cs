using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.Protocol.Tool;

IFileSystem fileSystem = new FileSystem();
return CommandLine.Run(args, Console.Out, Console.Error, fileSystem);
