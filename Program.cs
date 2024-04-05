using System;
using System.IO;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        public byte levelScalingBreakPoint;
        public byte scaleLeftDepth;
        public byte scaleRightDepth;

                                                    //public byte             bit #
                                                    // #     6   5   4   3   2   1   0   param A       range  param B       range
                                                    //----  --- --- --- --- --- --- ---  ------------  -----  ------------  -----
        public byte scaleCurve;   // 11    0   0   0 |  RC   |   LC  | SCL LEFT CURVE 0-3   SCL RGHT CURVE 0-3
        public byte DT_RS;        // 12  |      DET      |     RS    | OSC DETUNE     0-14  OSC RATE SCALE 0-7
        public byte VEL_AMS;      // 13    0   0 |    KVS    |  AMS  | KEY VEL SENS   0-7   AMP MOD SENS   0-3
        public byte outputLevel;       
        
        public byte FC_M;         // 15    0 |         FC        | M | FREQ COARSE    0-31  OSC MODE       0-1
        public byte frequencyFine;
    }

    
    [StructLayout(LayoutKind.Sequential, Size=128, Pack=1)]  
    struct PackedVoice
    {
        public PackedVoice() {}
        public PackedOperator[] ops = new PackedOperator[6];

        public byte pitchEGR1=0;
        public byte pitchEGR2=0;
        public byte pitchEGR3=0;
        public byte pitchEGR4=0;
        public byte pitchEGL1=0;
        public byte pitchEGL2=0;
        public byte pitchEGL3=0;
        public byte pitchEGL4=0;
     
                                                        //public byte             bit #
                                                        // #     6   5   4   3   2   1   0   param A       range  param B       range
                                                        //----  --- --- --- --- --- --- ---  ------------  -----  ------------  -----
        public byte algorithm=0;     //110    0   0 |        ALG        | ALGORITHM     0-31
        public byte KEYSYNC_FB=0;    //111    0   0   0 |OKS|    FB     | OSC KEY SYNC  0-1    FEEDBACK      0-7

        public byte lfoSpeed=0;
        public byte lfoDelay=0;
        public byte lfoPMD=0;
        public byte lfoAMD=0;
        public byte lfoPackedOpts=0; //116  |  LPMS |      LFW      |LKS| LF PT MOD SNS 0-7   WAVE 0-5,  SYNC 0-1

        public byte transpose=0;
        
        // byte[] name;//=Encoding.ASCII.GetBytes("No Name   ");

        public string name = "_Untitled_";
        // string Name {get=> Encoding.ASCII.GetString(name);}
    }

    // [StructLayout(LayoutKind.Sequential)] 
    struct DX7Sysex
    {
        public DX7Sysex() {}

        public byte sysexBegin = 0xF0;
        public byte vendorID = 0x43;
        public byte subStatusAndChannel=0;  // 0
        public byte format=9;
        public byte sizeMSB=0x20;  // 7 bits
        public byte sizeLSB=0x00;  // 7 bits

        public PackedVoice[] voices = new PackedVoice[32];

        public byte checksum=0;
        public byte sysexEnd=0xF7;  // 0xF7

        [JsonIgnore] public byte[] rawdata=new byte[4096];
    };

    class Program
    {
        const string VERSION = "1.0";
        const int FILE_SIZE = 4104;

        static CommandOption version;
        static CommandOption verbose;
        static CommandOption deDupe;
        static CommandOption<int?> patch;



        // ***************************************************************************

        static void ProcessOptions(CommandLineApplication app)
        {

            version = app.Option("-V|--version", "Displays the current version number.", CommandOptionType.NoValue);
            verbose = app.Option("-v|--verbose", "Displays a longer listing of dump info.", CommandOptionType.NoValue);
            deDupe = app.Option("-d|--find-dupes", "Finds duplicate voices in the bank.", CommandOptionType.NoValue);
            patch = app.Option<int?>("-p|--patch <PATCHNUM>", "Specify the voice patch to display info for.", CommandOptionType.SingleValue);
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
                    Environment.Exit(0);
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
                            Console.WriteLine(JsonSerializer.Serialize(sysex, opts));
                        }
                    }

                } catch (Exception e) {
                    Console.WriteLine(e.Message);
                }

            });

            return app.Execute(args);
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

        static byte[] getBytes(PackedVoice voice) {
            int size = Marshal.SizeOf(voice);
            byte[] arr = new byte[size];

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(voice, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
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
                
                //Do operators
                for(int i=0; i<voice.ops.Length; i++)
                {
                    var op= voice.ops[i];
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
