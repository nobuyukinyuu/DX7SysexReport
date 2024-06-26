﻿using System;
using System.IO;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using System.Runtime.InteropServices;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;


namespace DX7SysexReport
{

    // [StructLayout(LayoutKind.Explicit, Size=17, Pack=1)]  
    struct PackedOperator
    {
        //Rates
        public byte EG_R1;
        public byte EG_R2;
        public byte EG_R3;
        public byte EG_R4;

        //Levels
        public byte EG_L1;
        public byte EG_L2;
        public byte EG_L3;
        public byte EG_L4;
        [JsonIgnore] public byte levelScalingBreakPoint;
        public string LevelScalingBreakPoint {get=> Program.NoteName(levelScalingBreakPoint);}
        public byte scaleLeftDepth;
        public byte scaleRightDepth;

                                                //public byte             bit #
                                                // #     6   5   4   3   2   1   0   param A       range  param B       range
                                                //----  --- --- --- --- --- --- ---  ------------  -----  ------------  -----
        [JsonIgnore] public byte scaleCurve;    // 11    0   0   0 |  RC   |   LC  | SCL LEFT CURVE 0-3   SCL RGHT CURVE 0-3
        [JsonIgnore] public byte DT_RS;         // 12  |      DET      |     RS    | OSC DETUNE     0-14  OSC RATE SCALE 0-7
        [JsonIgnore] public byte VEL_AMS;       // 13    0   0 |    KVS    |  AMS  | KEY VEL SENS   0-7   AMP MOD SENS   0-3
        public byte outputLevel;
        
        [JsonIgnore] public byte FC_M;          // 15    0 |         FC        | M | FREQ COARSE    0-31  OSC MODE       0-1
        [JsonIgnore] public byte frequencyFine;


        readonly public string CurveScaleLeft { get=> ((CurveScaleType)(scaleCurve & 3)).ToString(); }
        readonly public string CurveScaleRight { get=> ((CurveScaleType)((scaleCurve >> 2) & 3)).ToString(); }

        readonly public int Detune { get=> ((DT_RS>>3) & 0xF) -7; }
        readonly public int RateScale { get=> DT_RS & 0x7; }

        readonly public int VelocitySensitivity { get=> (VEL_AMS >> 2) & 0x7; }
        readonly public int AMS  { get=> VEL_AMS & 0x3; }


        readonly public string FrequencyMode {get=> ((OscModes)(FC_M & 1)).ToString();}
        readonly public int CoarseFrequency {get=> (FC_M >> 1) & 31;}
        readonly public int FineFrequency {get=> frequencyFine;}

    }

    enum OscModes {Ratio, Fixed}
    enum CurveScaleType {LinMinus, ExpMinus, ExpPlus, LinPlus}
    
    [StructLayout(LayoutKind.Sequential, Size=128, Pack=1)]  
    struct PackedVoice
    {
        public PackedVoice() {}
        public PackedOperator[] ops = new PackedOperator[6];

        public string Name {get=> name;}

        public byte pitchEGR1=0;
        public byte pitchEGR2=0;
        public byte pitchEGR3=0;
        public byte pitchEGR4=0;
        public byte pitchEGL1=0;
        public byte pitchEGL2=0;
        public byte pitchEGL3=0;
        public byte pitchEGL4=0;
     

        public int Algorithm {get=>algorithm+1;}
                                                    //public byte             bit #
                                                    // #     6   5   4   3   2   1   0   param A       range  param B       range
                                                    //----  --- --- --- --- --- --- ---  ------------  -----  ------------  -----
        [JsonIgnore] public byte algorithm=0;       //110    0   0 |        ALG        | ALGORITHM     0-31
        [JsonIgnore] public byte KEYSYNC_FB=0;      //111    0   0   0 |OKS|    FB     | OSC KEY SYNC  0-1    FEEDBACK      0-7

        readonly public bool OscKeySync {get => ((KEYSYNC_FB>>3) & 1) == 1; }
        readonly public byte Feedback {get => (byte)(KEYSYNC_FB & 0x7); }

        public byte lfoSpeed=0;
        public byte lfoDelay=0;
        public byte lfoPMD=0;
        public byte lfoAMD=0;
        [JsonIgnore] public byte lfoPackedOpts=0;   //116  |  LPMS |      LFW      |LKS| LF PT MOD SNS 0-7   WAVE 0-5,  SYNC 0-1

        readonly public bool LFOKeySync {get=> (lfoPackedOpts & 1) == 1;}
        readonly public string LFOWaveform {get=>  ((LFOWaves)((lfoPackedOpts >> 1) & 0x7)).ToString();}
        readonly public int LFO_PMS {get=> (lfoPackedOpts >> 4);}

        public byte transpose=0;
        
        // byte[] name;//=Encoding.ASCII.GetBytes("No Name   ");

        [JsonIgnore] public string name = "_Untitled_";
    }

    enum LFOWaves {Triangle, SawDown, SawUp, Square, Sine, SAndHold}

    // [StructLayout(LayoutKind.Sequential)] 
    struct DX7Sysex
    {
        public DX7Sysex() {}

        [JsonIgnore] public byte sysexBegin = 0xF0;
        [JsonIgnore] public byte vendorID = 0x43;
        [JsonIgnore] public byte subStatusAndChannel=0;  // 0
        [JsonIgnore] public byte format=9;
        [JsonIgnore] public byte sizeMSB=0x20;  // 7 bits
        [JsonIgnore] public byte sizeLSB=0x00;  // 7 bits

        public PackedVoice[] voices = new PackedVoice[32];

        [JsonIgnore] public byte checksum=0;
        [JsonIgnore] public byte sysexEnd=0xF7;  // 0xF7

        [JsonIgnore] public byte[] rawdata=new byte[4096];
    };

    class Program
    {
        const string VERSION = "1.1";
        const int FILE_SIZE = 4104;

        static CommandOption version;
        static CommandOption verbose;
        static CommandOption deDupe;
        static CommandOption<int?> patch;



        // ***************************************************************************

        const int NOTE_C3 = 0x27; //39
        readonly static string[] noteNames = {"A-", "A#", "B-", "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#"};

        public static String NoteName(int noteNum=NOTE_C3)
        {
            int octave = (noteNum-3) / 12;
            return $"{noteNames[(noteNum) % 12]}{octave}";
        }

        static void ProcessOptions(CommandLineApplication app)
        {

            version = app.Option("-V|--version", "Displays the current version number.", CommandOptionType.NoValue);
            verbose = app.Option("-v|--verbose", "Displays a longer listing of dump info in JSON format.", CommandOptionType.NoValue);
            deDupe = app.Option("-d|--find-dupes", "Finds duplicate voices in the bank.", CommandOptionType.NoValue);
            patch = app.Option<int?>("-p|--patch <PATCHNUM>", "Specify the voice patch to display info for (Can be specified multiple times).", CommandOptionType.MultipleValue);
            patch.DefaultValue = -1;
        }


		// ***************************************************************************

        public static int Main(string[] args)
        {
			var app = new CommandLineApplication();
			app.Name = "DX7SysexReport";
			app.Description = "DX7 Sysex file information dumper";
			app.HelpOption("-?|-h|--help");
            app.UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect;

            ProcessOptions(app);

            app.OnExecute(() =>
            {
                if(version.HasValue())
                {
                    Console.WriteLine($"{System.AppDomain.CurrentDomain.FriendlyName} {VERSION}");
                }

                string filename="";
                if(app.RemainingArguments != null && app.RemainingArguments.Count==1)
                    filename = app.RemainingArguments[0];
                else
                    Console.WriteLine("Please specify a filename.");

                try
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
                        {
                            if (stream.Length != FILE_SIZE)
                            {
                                Console.WriteLine($"File specified not correct size! (Expecting {FILE_SIZE}, got {stream.Length})");
                                Environment.Exit(1);
                            }

                            DX7Sysex sysex = Parse(reader);
                            Validate(sysex);
                            var opts = new JsonSerializerOptions {IncludeFields=true, WriteIndented=true};

                            if(!verbose.HasValue() && patch.ParsedValue==patch.DefaultValue)
                                for(int i=0; i < sysex.voices.Length; i++)
                                    Console.WriteLine(ShortVoiceName(sysex,i));
                            else
                                if (patch.ParsedValue!=patch.DefaultValue)  
                                    foreach(int? val in patch.ParsedValues)
                                    {
                                        if (val==null) continue;
                                         Console.WriteLine($"{ShortVoiceName(sysex,(int)val)}:");
                                        Console.WriteLine(JsonSerializer.Serialize(sysex.voices[(int)val], opts));
                                        Console.WriteLine();
                                    }
                                else
                                    Console.WriteLine(JsonSerializer.Serialize(sysex, opts));


                            if(deDupe.HasValue()) FindDupes(sysex);
                        }

                    }

                } catch (Exception e) {
                    Console.WriteLine(e.Message);
                }

            });

            return app.Execute(args);
        }
        
        const string GR = "\x1b[32m";
        const string CY = "\x1b[33m";
        const string RS = "\x1b[39m";
        private static string ShortVoiceName(DX7Sysex sysex, int index) =>
            $"{GR}Voice {CY}#{index.ToString("00")}{GR}:{RS} {sysex.voices[index].name}  \x1b[34m(Algorithm {sysex.voices[index].Algorithm}){RS}";

        private static void FindDupes(DX7Sysex sysex)
        {
            Console.WriteLine();

            var toCheck = new HashSet<int>();
            if (patch.ParsedValue!=patch.DefaultValue)  
                foreach(int? val in patch.ParsedValues)
                    toCheck.Add((int)val);
            else
                for(int i=0; i<sysex.voices.Length; i++)
                    toCheck.Add(i);


            var dupesFound = new HashSet<int>();
            for(int i=0; i<sysex.voices.Length-1; i++)
            {
                if(!toCheck.Contains(i) || dupesFound.Contains(i)) continue;

                var firstDupe = false;
                var Voice1 = sysex.voices[i];
                    Voice1.name = ""; //Match dupes with different names
                var v1s = JsonSerializer.Serialize(Voice1, new JsonSerializerOptions{IncludeFields=true});
                for(int j=i+1; j<sysex.voices.Length; j++)
                {
                    if(!toCheck.Contains(j)) continue;
                    var Voice2 = sysex.voices[j];
                    Voice2.name = "";
                    var v2s = JsonSerializer.Serialize(Voice2, new JsonSerializerOptions{IncludeFields=true});

                    if (v1s == v2s)
                    {
                        if (!firstDupe)
                            Console.WriteLine($"Dupes of {ShortVoiceName(sysex,i)}:");
                            firstDupe=true;
                        Console.WriteLine($"    {ShortVoiceName(sysex, j)}");
                        dupesFound.Add(j);  //Mark this voice as a found dupe so the i indexer can skip checking it again.
                    }
                }
            }
            if (dupesFound.Count==0) Console.WriteLine("No duplicate voices found.");
        }

        private static int Validate(DX7Sysex sysex)
        {
            // *** Verify Header and Footer ***
            
            if (sysex.sysexBegin != 0xF0)
            {
                Console.WriteLine("Did not find sysex start byte 0xF0..");
                return 1;
            }
            if (sysex.vendorID != 0x43)
            {
                Console.WriteLine($"Did not find Vendor ID.. Expected  Yamaha (0x43), got {sysex.vendorID}");
                return 1;
            }
            if (sysex.subStatusAndChannel != 0)
            {
                Console.WriteLine("Did not find substatus 0 and channel 1..");
                return 1;
            }
            if (sysex.format != 0x09)
            {
                Console.WriteLine("Did not find format 9 (32 voices)..");
                return 1;
            }
            if (sysex.sizeMSB != 0x20  ||  sysex.sizeLSB != 0)
            {
                Console.WriteLine("Did not find size 4096");
                return 1;
            }
            if (sysex.sysexEnd != 0xF7)
            {
                Console.WriteLine("Did not find sysex end byte 0xF7..");
                return 1;
            }


            // **** checksum ****
            // Start of 4096 byte data block.
            byte sum = 0;
            var p= new byte[4096];
            p = sysex.rawdata;
            // for(int i=0; i<32; i++)
            // {
            //     Array.Copy(getBytes(sysex.voices[i]), 0, p, i*128, 128);
            // }

            for (int i=0; i<4096; i++)
            {
                sum += (byte)(p[i] & 0x7F);
            }
            // Two's complement: Flip the bits and add 1
            sum = (byte)((~sum) + 1);
            // Mask to 7 bits
            sum &= 0x7F;
            if (sum != sysex.checksum)
            {
                Console.WriteLine($"CHECKSUM FAILED: Produced {sum} from the raw data, but expected {sysex.checksum}");
                return 1;
            }
            
            return 0;

        }
        private static DX7Sysex Parse(BinaryReader reader)
        {
            var o = new DX7Sysex();
            o.sysexBegin = reader.ReadByte();
            o.vendorID = reader.ReadByte();
            o.subStatusAndChannel = reader.ReadByte();
            o.format = reader.ReadByte();
            o.sizeMSB = reader.ReadByte();
            o.sizeLSB = reader.ReadByte();

            //Do voices
            for(int v=0; v<o.voices.Length; v++)
            {
                PackedVoice voice = new PackedVoice();
                
                //Do operators.  Sysex format packs it starting with Op6(!) and going in reverse back to Op1.
                for(int i=0; i<voice.ops.Length; i++)
                {
                    var op= voice.ops[voice.ops.Length-i-1];
                    op.EG_R1 = reader.ReadByte();
                    op.EG_R2 = reader.ReadByte();
                    op.EG_R3 = reader.ReadByte();
                    op.EG_R4 = reader.ReadByte();

                    //Levels
                    op.EG_L1 = reader.ReadByte();
                    op.EG_L2 = reader.ReadByte();
                    op.EG_L3 = reader.ReadByte();
                    op.EG_L4 = reader.ReadByte();
                    op.levelScalingBreakPoint = reader.ReadByte();
                    op.scaleLeftDepth = reader.ReadByte();
                    op.scaleRightDepth = reader.ReadByte();

                    op.scaleCurve = reader.ReadByte();
                    op.DT_RS = reader.ReadByte();
                    op.VEL_AMS = reader.ReadByte();
                    op.outputLevel = reader.ReadByte();
                    
                    op.FC_M = reader.ReadByte();
                    op.frequencyFine = reader.ReadByte();

                    voice.ops[voice.ops.Length-i-1] = op;
                }

                voice.pitchEGR1 = reader.ReadByte();
                voice.pitchEGR2 = reader.ReadByte();
                voice.pitchEGR3 = reader.ReadByte();
                voice.pitchEGR4 = reader.ReadByte();
                voice.pitchEGL1 = reader.ReadByte();
                voice.pitchEGL2 = reader.ReadByte();
                voice.pitchEGL3 = reader.ReadByte();
                voice.pitchEGL4 = reader.ReadByte();
            
                voice.algorithm = reader.ReadByte();
                voice.KEYSYNC_FB = reader.ReadByte();

                voice.lfoSpeed = reader.ReadByte();
                voice.lfoDelay = reader.ReadByte();
                voice.lfoPMD = reader.ReadByte();
                voice.lfoAMD = reader.ReadByte();
                voice.lfoPackedOpts = reader.ReadByte();

                voice.transpose = reader.ReadByte();
                
                voice.name = Encoding.ASCII.GetString(reader.ReadBytes(10));

                o.voices[v] = voice;
            }
            
            o.checksum = reader.ReadByte();
            o.sysexEnd=reader.ReadByte();  // 0xF7
            
            reader.BaseStream.Seek(6,SeekOrigin.Begin);
            o.rawdata = reader.ReadBytes(4096);
 
            return o;
        }

    }
}
