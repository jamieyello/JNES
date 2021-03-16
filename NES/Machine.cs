using System;
using System.Collections.Generic;

/// <summary>
/// JNES is an emulator designed for educational purposes, designed with simplicity
/// and readibility in mind.
/// </summary>

namespace NES
{
    class Machine
    {
        bool show_debug = false;

        const int BYTES_IN_KILOBYTE = 0x400;
        byte EMPTY_BYTE = 0;

        /// <summary>
        /// The loaded ROM
        /// </summary>
        byte[] prg_rom;
        byte[] chr_rom;
        byte[] rom_header;
        byte[] ram;

        /// <summary>
        /// Pointer array to various memory addresses above. (Except copied, C# is annoying)
        /// </summary>
        byte[] cpu_mapped_memory;

        int prg_ram_start;
        int prg_ram_end;

        /// <summary>
        /// An array since the ROM can be segmented
        /// </summary>
        int[] prg_rom_start;
        int[] prg_rom_end;

        int chr_rom_start;
        int chr_rom_end;

        enum NesFileType {
            nothing,
            nes_2,
            ines,
            nes_arch
        }
        NesFileType nesFileType = NesFileType.nothing;

        bool flag_mirror;
        bool flag_cartridge_memory;
        bool flag_trainer;
        bool flag_ignore_mirroring;
        bool flag_vs_unisystem;
        bool flag_playchoice_10;
        bool flag_pal;

        byte mapper_type;


        /// <summary>
        /// In bytes
        /// </summary>
        int prg_ram_size;


        /// <summary>
        /// The A register (general purpose, 8-bit)
        /// </summary>
        byte A_reg_Accumulator;

        /// <summary>
        /// The X register (general purpose, 8-bit)
        /// </summary>
        byte X_reg;

        /// <summary>
        /// The Y register (general purpose, 8-bit)
        /// </summary>
        byte Y_reg;

        /// <summary>
        /// The P register (status, 8-bit)
        /// </summary>
        byte P_reg;

        /// <summary>
        /// The SP register (stack pointer, 8-bit) (not really used)
        /// </summary>
        byte stack_pointer;

        /// <summary>
        /// The PC register (program counter, 16-bit)
        /// </summary>
        int program_counter;

        /// <summary>
        /// Set to true to skip a cpu cycle. Used by opcode EA
        /// </summary>
        bool cpu_nop;

        List<int> stack= new List<int>();

        ///<summary>
        ///<para>THE STATUS REGISTER http://nesdev.com/6502.txt</para>
        ///
        ///<br>     This register consists of eight "flags" (a flag = something that indi-</br>
        ///<br>cates whether something has, or has not occurred). Bits of this register</br>
        ///<br>are altered depending on the result of arithmetic and logical operations.</br>
        ///<br>These bits are described below:</br>
        ///<br></br>
        ///<br>     Bit No.       7   6   5   4   3   2   1   0</br>
        ///<br>                   S   V       B   D   I   Z   C</br>
        /// </summary>

        // While this should be a byte like the rest of the registers, using a struct with booleans is easier/faster
        //to use.
        //byte status_flags;

        struct StatusFlagsRegister {
            public bool S_Negative;
            public bool V_Overflow;
            public bool Break;
            public bool Decimal_Mode;
            public bool Interrupt_Disable;
            public bool Zero;
            public bool Carry;
        } StatusFlagsRegister status_flags;

        /// <summary>
        ///   THE ACCUMULATOR http://nesdev.com/6502.txt
        /// 
        ///         This is THE most important register in the microprocessor.Various ma-
        ///   chine language instructions allow you to copy the contents of a memory
        ///   location into the accumulator, copy the contents of the accumulator into
        ///   a memory location, modify the contents of the accumulator or some other
        ///   register directly, without affecting any memory. And the accumulator is
        ///   the only register that has instructions for performing math.
        ///   
        /// </summary>
        byte accumulator;

        public Machine()
        {
        }

        ~Machine()
        {
          
        }

        public int Initiate(String file)
        {
            try
            {
                LoadRom(file);
                ram = new byte[BYTES_IN_KILOBYTE * 2];
                cpu_mapped_memory = new byte[0xFFFF];
                SetMapper();
            }
            catch (Exception e)
            {
                Console.WriteLine("Fatal error in initialization: " + e.ToString());
                return -1;
            }
            
            return prg_rom.Length;
        }

        private void LoadRom(String file)
        {
            // http://wiki.nesdev.com/w/index.php/INES

            Console.WriteLine("Loading " + file);
            byte[] nesFile = System.IO.File.ReadAllBytes(file);

            int file_pos = 0;

            // Load header:
            rom_header = new byte[16];
            Buffer.BlockCopy(nesFile, file_pos, rom_header, 0, 16);
            file_pos += 16;

            Console.WriteLine("Rom header: ");
            for (int i = 0; i < 16; i++)
            {
                Console.WriteLine(i.ToString() + " " + rom_header[i].ToString("X"));
            }

            if ((rom_header[7] == 8) && (rom_header[12] == 8)) {
                Console.WriteLine("Nes 2.0 detected");
                nesFileType = NesFileType.nes_2;
            }

            if ((rom_header[7] == 0) && (rom_header[12] == 0))
            {
                Console.WriteLine("iNES detected");
                nesFileType = NesFileType.ines;
            }

            if (nesFileType == NesFileType.nothing) {
                Console.WriteLine("Archaic ines detected");
                nesFileType = NesFileType.nes_arch;
            }

            // Parse header:
            // 0-3: Constant $4E $45 $53 $1A ("NES" followed by MS-DOS end-of-file)
            // 4: Size of PRG ROM in 16 KB units
            int prg_size = rom_header[4] * BYTES_IN_KILOBYTE * 16;

            // 5: Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
            int chr_size = rom_header[5] * BYTES_IN_KILOBYTE * 8;

            // 6: flags
            {
                // 0 - 0: horizontal (vertical arrangement) (CIRAM A10 = PPU A11) 1: vertical (horizontal arrangement) (CIRAM A10 = PPU A10)
                flag_mirror = BinaryTools.GetBit(rom_header[6], 0);

                // 1 - 1: Cartridge contains battery-backed PRG RAM ($6000-7FFF) or other persistent memory
                flag_cartridge_memory = BinaryTools.GetBit(rom_header[6], 1);

                // 2 - 1: 512-byte trainer at $7000-$71FF (stored before PRG data)
                flag_trainer = BinaryTools.GetBit(rom_header[6], 2);

                // 3 - 1: Ignore mirroring control or above mirroring bit; instead provide four-screen VRAM
                flag_ignore_mirroring = BinaryTools.GetBit(rom_header[6], 3);

                // 4-7 - Lower nybble of mapper number
                for (byte i = 0; i < 4 ; i++)
                {
                    mapper_type += Convert.ToByte(
                        Convert.ToByte(BinaryTools.GetBit(rom_header[6], 4 + i)) * (2 ^ i)
                        );
                }
            }

            // 7: flags
            {
                // 0 - VS Unisystem
                flag_vs_unisystem = BinaryTools.GetBit(rom_header[7], 0);

                // 1 - PlayChoice-10 (8KB of Hint Screen data stored after CHR data)
                flag_playchoice_10 = BinaryTools.GetBit(rom_header[7], 1);

                // 2-3 - If equal to 2, flags 8-15 are in NES 2.0 format
                // (instructions unclear, ignoring)

                // 4-7 - Upper nybble of mapper number
                for (byte i = 0; i < 4; i++)
                {
                    mapper_type += Convert.ToByte(
                        Convert.ToByte(BinaryTools.GetBit(rom_header[7], 4 + i)) * (2 ^ (i + 4))
                        );
                }

                Console.WriteLine("Mapper number calculated to be " + mapper_type.ToString());
            }

            // 8: Size of PRG RAM in 8 KB units (Value 0 infers 8 KB for compatibility; see PRG RAM circuit)
            prg_ram_size = rom_header[8] * BYTES_IN_KILOBYTE * 8;
            if (prg_ram_size == 0) { prg_ram_size = BYTES_IN_KILOBYTE * 8; }

            // 9: flags
            {
                // 0 - TV system (0: NTSC; 1: PAL)
                flag_pal = BinaryTools.GetBit(rom_header[9], 0);

                // 0-7 Reserved, set to zero
            }

            // 10: flags (unofficial, skipped)

            // 11-15: Zero filled

            // Load trainer (skipped)
            if (flag_trainer) {
                Console.WriteLine("Warning: Trainer support not implemented.");
                file_pos += 512;
            }

            // Load PRG ROM
            prg_rom = new byte[prg_size];
            Buffer.BlockCopy(nesFile, file_pos, prg_rom, 0, prg_size);
            file_pos += prg_size;

            Console.WriteLine(".nes file size = " + nesFile.Length.ToString());
            Console.WriteLine("Program size = " + prg_rom.Length.ToString());

            // Load CHR ROM
            chr_rom = new byte[chr_size];
            Buffer.BlockCopy(nesFile, file_pos, chr_rom, 0, chr_size);
            file_pos += chr_size;
            Console.WriteLine("CHR size = " + chr_rom.Length.ToString());

            // Playchoice INST-ROM, if present (0 or 8192 bytes)

            // PlayChoice PROM, if present(16 bytes Data, 16 bytes CounterOut)(this is often missing, see PC10 ROM - Images for details)
        }

        private void SetMapper()
        {
            // https://wiki.nesdev.com/w/index.php/Mapper

            // Set to default
            prg_rom_start = new int[0];
            prg_rom_end = new int[0];
            prg_ram_start = 0x0000;
            prg_ram_end = 0x0000;
            chr_rom_start = 0x0000;
            chr_rom_end = 0x0000;

            bool mirror = false;

            switch (mapper_type) {
                case 0:
                    prg_rom_start = new int[2];
                    prg_rom_end = new int[2];

                    prg_ram_start = 0x6000;
                    prg_ram_end = 0x7FFF;
                    prg_rom_start[0] = 0x8000;
                    prg_rom_end[0] = 0xBFFF;
                    prg_rom_start[1] = 0xC000;
                    prg_rom_end[1] = 0xFFFF;
                    chr_rom_start = 0x0000;
                    chr_rom_end = 0x1FFF;
                    
                    mirror = prg_rom.Length == 16384;

                    if (mirror) { Console.WriteLine("mirrored"); }
                    break;
                case 5: // MMC5
                    // Get PRG mode
                    switch (prg_rom[0x5100]) {
                        case 0:
                            prg_rom_start = new int[1];
                            prg_rom_end = new int[1];

                            prg_rom_start[0] = 0x8000;
                            prg_rom_end[0] = 0xFFFF;
                            break;
                        default:
                            throw new Exception("Invalid ROM: (Error while mapping MMC5, unimplemented mode)");
                    }

                    //throw new Exception("Unsupported mapper 5.");
                    break;
                default:
                    throw new Exception("Unsupported mapper.");
            }

            // Map the PRG ROM
            int pr = 0;
            for (int rom_map = 0; rom_map < prg_rom_start.Length; rom_map++)
            {
                if (mirror) { pr = 0; }
                for (int i = prg_rom_start[rom_map]; i < prg_rom_end[rom_map]; i++)
                {
                    cpu_mapped_memory[i] = prg_rom[pr++];
                }

                Console.WriteLine("mapped rom addresses = " + pr);
                if (pr == prg_rom.Length) { break; }
            }

            // Map the CHR ROM
            int cr = 0;
            for (int i = chr_rom_start; i < chr_rom_end; i++)
            {
                cpu_mapped_memory[i] = chr_rom[cr];
                cr++;
            }

            program_counter = Get32BitValue(0xFFFC);
        }

        /// <summary>
        /// Meant to be called once per frame (every 1/60th second)
        /// </summary>
        /// <param name="input"></param>
        public void Step(InputFrame input)
        {

            CPUTick();
            // Master Clock: 21,477,272 ticks per second, 357954.533333 per frame
            for (int i = 0; i < 357955; i++)
            {
                // CPU, master clock divided by 12
                if ((i % 12) == 0)
                {
                    CPUTick();
                }
            }
        }

        private void CPUTick()
        {
            if (program_counter == 0xFFFF) { Console.WriteLine("Error: Reached end of memory."); return; }
            if (cpu_nop) { Console.WriteLine("Skipped CPU cycle."); cpu_nop = false; return; }

            Debug("");

            // Hack, skip opcode 0s
            int skipped_0_opcode = 0;
            while (cpu_mapped_memory[program_counter] == 0) { program_counter++; skipped_0_opcode++; }
            if (skipped_0_opcode > 0) { Debug("Skipped " + skipped_0_opcode + " BRK opcode(s) (00)"); }

            byte code = cpu_mapped_memory[program_counter++];
            Debug("Executing code " + String.Format("{0:X}", code) + " at address " + String.Format("{0:X}", program_counter - 1));

            // http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php/6502_Opcodes
            // http://www.6502.org/tutorials/6502opcodes.html
            // http://www.obelisk.me.uk/6502/reference.html
            switch (code) {
                case 0x10: // BPL (Branch on PLus)
                    if (!status_flags.S_Negative) { program_counter += cpu_mapped_memory[program_counter]; }
                    else { program_counter++; }
                    break;

                case 0x20: // JSR, jump to new location, save return address on stack. Fixed three bytes.
                    int return_address = program_counter + 2;
                    stack.Add(return_address);
                    program_counter = Get32BitValue(ref program_counter);
                    Debug("JSR jumping to " + String.Format("{0:X}", program_counter));
                    break; // Working

                case 0x2C: // BIT (Absolute), fixed 3 bytes
                    int address = Get32BitValue(ref program_counter);
                    status_flags.S_Negative = cpu_mapped_memory[address].GetBit(7);
                    status_flags.V_Overflow = cpu_mapped_memory[address].GetBit(6);
                    status_flags.Zero = (A_reg_Accumulator & cpu_mapped_memory[address]) == 0;
                    break; // Working

                case 0x29:
                    A_reg_Accumulator &= cpu_mapped_memory[program_counter++];
                    status_flags.Zero = A_reg_Accumulator == 0;
                    status_flags.S_Negative = A_reg_Accumulator.GetBit(7);
                    break;

                case 0x4C: // JMP Absolute
                    program_counter = Get32BitValue(ref program_counter);
                    break;

                case 0x78: // SEI, Set the interrupt disable flag to one.
                    status_flags.Interrupt_Disable = true;
                    break;

                case 0x85: // STA Zero Page
                    cpu_mapped_memory[cpu_mapped_memory[program_counter++]] = A_reg_Accumulator;
                    break;

                case 0x86: // STX Zero Page
                    cpu_mapped_memory[cpu_mapped_memory[program_counter++]] = X_reg;
                    break;

                case 0x88: // DEY, Subtracts one from the Y register setting the zero and negative flags as appropriate.
                    Y_reg--;
                    status_flags.Zero = Y_reg == 0;
                    status_flags.S_Negative = Y_reg.GetBit(7);
                    break;

                case 0x8D: // STA, Stores the contents of the accumulator into memory.
                    cpu_mapped_memory[Get32BitValue(ref program_counter)] = A_reg_Accumulator;
                    break;

                //case 0x91:
                    //break;

                case 0x9A: // TXS, Copies the current contents of the X register into the stack register.
                    stack.Add(X_reg);
                    break;

                case 0xA0: // LDY Immediate
                    Y_reg = cpu_mapped_memory[program_counter++];
                    status_flags.Zero = Y_reg == 0;
                    status_flags.S_Negative = Y_reg.GetBit(7);
                    break;

                case 0xA2: // LDX Immediate, Loads a byte of memory into the X register setting the zero and negative flags as appropriate.
                    X_reg = cpu_mapped_memory[program_counter++];
                    status_flags.Zero = X_reg == 0;
                    status_flags.S_Negative = X_reg.GetBit(7);
                    break;

                case 0xA9: // LDA Immediate
                    A_reg_Accumulator = cpu_mapped_memory[program_counter++];
                    status_flags.Zero = A_reg_Accumulator == 0;
                    status_flags.S_Negative = A_reg_Accumulator.GetBit(7);
                    break;

                case 0xAD: // LDA Absolute
                    A_reg_Accumulator = cpu_mapped_memory[Get32BitValue(ref program_counter)];
                    status_flags.Zero = A_reg_Accumulator == 0;
                    status_flags.S_Negative = A_reg_Accumulator.GetBit(7);
                    break;

                case 0xB1: // LDA Indirect, Y

                    break;

                case 0xBD: // LDA Absolute, X
                    A_reg_Accumulator = cpu_mapped_memory[Get32BitValue(ref program_counter)];
                    A_reg_Accumulator += X_reg; // Why tf can't I do this in the line above
                    status_flags.Zero = A_reg_Accumulator == 0;
                    status_flags.S_Negative = A_reg_Accumulator.GetBit(7);
                    break;

                case 0xD8: // CLD, Sets the decimal mode flag to zero.
                    status_flags.Decimal_Mode = false;
                    break;

                case 0xEA: // NOP, does nothing for 2 frames. One byte.
                    cpu_nop = true;
                    break;

                case 0xF0:
                    if (status_flags.Zero) { program_counter += cpu_mapped_memory[program_counter] - 127; }
                    program_counter++;
                    break;

                default:
                    Debug("Unsupported opcode: " + String.Format("{0:X}", code));
                    break;
            }
        }

        /// <summary>
        /// Get a 32 bit value from memory. Increments counter by 2.
        /// </summary>
        /// <param name="counter"></param>
        /// <returns></returns>
        private int Get32BitValue(ref int counter)
        {
            return cpu_mapped_memory[counter++] + cpu_mapped_memory[counter++] * 0x100;
        }

        private int Get32BitValue(int address)
        {
            return cpu_mapped_memory[address++] + cpu_mapped_memory[address] * 0x100;
        }

        private void Debug(String debug)
        {
            if (!show_debug) return;
            Console.WriteLine(debug);
        }
    }
}
