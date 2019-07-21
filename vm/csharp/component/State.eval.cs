﻿namespace vm.component
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using ancient.runtime;
    using ancient.runtime.emit.sys;
    using ancient.runtime.emit.@unsafe;
    using ancient.runtime.exceptions;
    using ancient.runtime.hardware;
    using MoreLinq;

	using static System.MathF;
    using Module = ancient.runtime.emit.sys.Module;

    public partial class State
    {
		
        public void Eval()
        {
            using var watcher = new StopwatchOperation("eval operation");
            MemoryManagement.FastWrite = fw;
            if (bf)
            {
                bus.debugger.handleBreak(u16 & pc, bus.cpu);
                mem[0x17] = 0x0;
            }
           
            switch (iid)
            {
                case 0x0:
                    trace("call :: skip");
                    break;
                case ushort opcode when opcode.In(0xD0..0xE8):
                    /* need @float-flag */
                    if(!ff) bus.cpu.halt(0xA9);
                    trace($"call :: [0xD0..0xE8]::0x{iid:X}");
                    var result = iid switch { // todo refactoring 
                        0xD0 => f32i64 & Abs    (i64f32 & mem[r1]),
                        0xD1 => f32i64 & Acos   (i64f32 & mem[r1]),
                        0xD2 => f32i64 & Atan   (i64f32 & mem[r1]),
                        0xD3 => f32i64 & Acosh  (i64f32 & mem[r1]),
                        0xD4 => f32i64 & Atanh  (i64f32 & mem[r1]),
                        0xD5 => f32i64 & Asin   (i64f32 & mem[r1]),
                        0xD6 => f32i64 & Asinh  (i64f32 & mem[r1]),
                        0xD7 => f32i64 & Cbrt   (i64f32 & mem[r1]),
                        0xD8 => f32i64 & Ceiling(i64f32 & mem[r1]),
                        0xD9 => f32i64 & Cos    (i64f32 & mem[r1]),
                        0xDA => f32i64 & Cosh   (i64f32 & mem[r1]),
                       
                        0xDB => f32i64 & Floor  (i64f32 & mem[r1]),
                        0xDC => f32i64 & Exp    (i64f32 & mem[r1]),
                        0xDD => f32i64 & Log    (i64f32 & mem[r1]),
                        0xDE => f32i64 & Log10  (i64f32 & mem[r1]),
                        0xDF => f32i64 & Tan    (i64f32 & mem[r1]),
                        0xE0 => f32i64 & Tanh   (i64f32 & mem[r1]),
                        
                        0xE4 => f32i64 & Atan2  (i64f32 & mem[r1], i64f32 & mem[r2]),
                        0xE5 => f32i64 & Min    (i64f32 & mem[r1], i64f32 & mem[r2]),
                        0xE6 => f32i64 & Max    (i64f32 & mem[r1], i64f32 & mem[r2]),

                        0xE7 => f32i64 & Sin    (i64f32 & mem[r1]),
                        0xE8 => f32i64 & Sinh   (i64f32 & mem[r1]),

                        0xE1 => f32i64 & Truncate(i64f32 & mem[r1]),
                        0xE2 => f32i64 & BitDecrement(i64f32 & mem[r1]),
                        0xE3 => f32i64 & BitIncrement(i64f32 & mem[r1]),
                        _ => throw new CorruptedMemoryException($"")
                    };
                    /* @stack-forward-flag */
                    if (sf)  stack.push(result);
                    else     mem[r1] = result;
                    break;
                #region halt
                case 0xF when r1 == 0xF && r2 == 0xF && r3 == 0xF && u1 == 0xF && u2 == 0xF && x1 == 0xF:
                    bus.cpu.halt(0xF);
                    break;
                case 0xD when r1 == 0xE && r2 == 0xA && r3 == 0xD:
                    bus.cpu.halt(0x0);
                    break;
                case 0xB when r1 == 0x0 && r2 == 0x0 && r3 == 0xB && u1 == 0x5:
                    bus.cpu.halt(0x1);
                    break;
                #endregion
                case 0x1 when x2 == 0x0:
                    trace($"call :: ldi 0x{u1:X}, 0x{u2:X} -> 0x{r1:X}");
                    _ = u2 switch
                    {
                        0x0 => mem[r1] = u1,
                        _   => mem[r1] = i64 & ((u2 << 4) | u1)
                    };
                    break;
                case 0x1 when x2 == 0xA:
                    trace($"call :: ldx 0x{u1:X}, 0x{u2:X} -> 0x{r1:X}-0x{r2:X}");
                    mem[((r1 << 4) | r2)] = i64 & ((u1 << 4) | u2);
                    break;
                case 0x3: // 0x3120000
                    trace($"call :: swap, 0x{r1:X}, 0x{r2:X}");
                    mem[r1] ^= mem[r2];
                    mem[r2] =  mem[r1] ^ mem[r2];
                    mem[r1] ^= mem[r2];
                    break;
                case 0xF when x2 == 0xE: // 0xF9988E0
                    trace($"call :: move, dev[0x{r1:X}] -> 0x{r2:X} -> 0x{u1:X}");
                    bus.Find(r1 & 0xFF).write(r2 & 0xFF, i32 & mem[u1] & 0xFF);
                    break;
                case 0xF when x2 == 0xC: // 0xF00000C
                    trace($"call :: move, dev[0x{r1:X}] -> 0x{r2:X} -> [0x{u1:X}-0x{u2:X}]");
                    bus.Find(r1 & 0xFF).write(r2 & 0xFF, (r3 << 12 | u1 << 8 | u2 << 4 | x1) & 0xFFFFFFF);
                    break;
                case 0xA4:
                    trace($"call :: rfd dev[0x{r1:X}], 0x{r2:X}");
                    stack.push(bus.Find(r1 & 0xFF).read(r2 & 0xFF));
                    break;
                case 0x8 when u2 == 0xC: // 0x8F000C0
                    trace($"call :: ref_t 0x{r1:X}");
                    mem[r1] = i64 & pc;
                    break;
                case 0xA0:
                    trace($"call :: orb '{r1}' times");
                    for (var i = pc + r1; pc != i;)
                        stack.push(i64 & fetch());
                    break;
                case 0xA1:
                    trace($"call :: pull -> 0x{r1:X}");
                    mem[r1] = stack.pop();
                    break;
                case 0xA5: /* @sig */
                    stack.push(mem[r2] = i64 & pc);
                    var frag = default(ulong?);
                    while (AcceptOpCode(frag) != 0xA6 /* @ret */)
                    {
                        var pc_r = stack.pop();
                        frag = next(i64 | pc_r++);
                        if (frag is null) 
                            bus.cpu.halt(0xA1);
                        stack.push(pc_r);
                    }
                    mem[r2 + 1] = stack.pop();
                    break;
                case 0xB1: /* @inc */
                    trace($"call :: increment 0x{r1:X}++");
                    unchecked { mem[r1]++; } 
                    break; 
                case 0xB2:  /* @dec */
                    trace($"call :: decrement 0x{r1:X}--");
                    unchecked { mem[r1]--; } 
                    break; 
                case 0xB3: /* @dup */
                    trace($"call :: dup 0x{(u2 << 4) | u1:X}");
                    mem[(u2 << 4) | u1] = mem[(r1 << 4) | r2];
                    break;
                case 0xB4: /* @ckft */
                    trace($"call :: ckft 0x{(r2 << 4) | r1:X}");
                    if (ff && !float.IsFinite(f32i64 & mem[(r2 << 4) | r1]))
                        bus.cpu.halt(0xA9);
                    break;
                case 0x36: /* @call */
                    d16u sign = (u8 & r3, u8 & u1, u8 & u2, u8 & x1);
                    trace($"call :: call 0x{sign.Value:X}");
                    var find = Module.Global.Find(sign, out var @extern);
                    if (find != ExternStatus.Found)
                    {
                        bus.cpu.halt(0xA16 + (int) find, $"0x{sign.Value:X}");
                        return;
                    }
                    trace($"call :: {@extern.Signature}");
                    CallAndWrite(@extern);
                    break;
                case 0x37: /* @prune */
                    evaluation.Prune();
                    break;
                case 0x38: /* @locals */
                    d8u len = (u8 & r1, u8 & r2);
                    evaluation.Alloc(len);
                    var segs = new List<EvaluationSegment>(len);
                    for(var i = 0; i != len; i++)
                        segs.Add(EvaluationSegment.Construct(fetch(), fetch()));
                    var @params = new object[len];
                    foreach (var s in segs)
                        @params[s.Index] = Activator.CreateInstance(Type.GetType($"ancient.runtime.emit.sys.{s.Type}_Type"));
                    locals.Pin(@params, out var @external);
                    foreach (var (host, index) in @external.Select((x, i) => (x, i)))
                        evaluation[index] = host;
                    break;
                case 0xB5: /* @ixor */
                case 0xB6: /* @ior */
                    d8u first  = (u8 & r1, u8 & r2);
                    d8u second = (u8 & u1, u8 & u2);
                    // not support float-mode
                    if(iid == 0xB5)
                        mem[first] ^= mem[second];
                    if(iid == 0xB6)
                        mem[first] |= mem[second];
                    break;
                #region debug

                case 0xF when x2 == 0xF: /* @mvx */
                    var x = mem[u1].ToString();

                    if (ff) x = (i64f32 & mem[u1]).ToString(CultureInfo.InvariantCulture);

                    static short[] cast(string str)
                    {
                        var list = new List<int>(); 
                        foreach (var c in str)
                        {
                            var uu1 = (c & 0xF0) >> 4;
                            var uu2 = (c & 0xF);
                            list.Add((uu1 << 4 | uu2) & 0xFFFFFFF);
                        }
                        return list.Select(i32i16.auto).ToArray();
                    }
                    foreach (var uuu in cast(x))
                        bus.Find(r1 & 0xFF).write(r2 & 0xFF, uuu);
                    break;

                case 0xC1 when x2 == 0x1: /* @break :: now */
                    trace($"[0x{iid:X}] @break :: now");
                    bus.debugger.handleBreak(u16 & pc, bus.cpu);
                    mem[0x17] = 0x0;
                    break;
                case 0xC1 when x2 == 0x2: /* @break :: next */
                    trace($"[0x{iid:X}] @break :: next");
                    mem[0x17] = 0x1;
                    break;
                case 0xC1 when x2 == 0x3: /* @break :: after */
                    trace($"[0x{iid:X}] @break :: after");
                    mem[0x17] = 0x3;
                    break;

                #endregion
                #region jumps

                case 0x8 when u2 == 0xF && x1 == 0x0: /* @jump_t */
                    trace($"jump_t 0x{r1:X}");
                    pc = (ulong)mem[r1];
                    break;
                case 0x8 when u2 == 0xF && x1 == 0x1: /* @jump_e  0x8FCD0F10 */
                    trace(mem[r2] >= mem[r3]
                        ? $"jump_e 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> apl"
                        : $"jump_e 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> skip");
                    if(mem[r2] >= mem[r3]) 
                        pc = i64 | mem[r1];
                    break;
                case 0x8 when u2 == 0xF && x1 == 0x2: /* @jump_g */
                    trace(mem[r2] > mem[r3]
                        ? $"jump_g 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> apl"
                        : $"jump_g 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> skip");
                    if(mem[r2] > mem[r3])
                        pc = (ulong)mem[r1];
                    break;
                case 0x8 when u2 == 0xF && x1 == 0x3: /* @jump_u */
                    trace(mem[r2] < mem[r3]
                        ? $"jump_u 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> apl"
                        : $"jump_u 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> skip");
                    if(mem[r2] < mem[r3])
                        pc = (ulong)mem[r1];
                    break;
                case 0x8 when u2 == 0xF && x1 == 0x4: /* @jump_y */
                    trace(mem[r2] <= mem[r3]
                        ? $"jump_y 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> apl"
                        : $"jump_y 0x{r1:X} -> 0x{r2:X} 0x{r3:X} -> skip");
                    if(mem[r2] <= mem[r3]) pc = (ulong)mem[r1];
                    break;
                case 0x09: /* @jump */
                    pc = (ulong)((r1 << 4) | r2);
                    break;

                #endregion
                #region math 
                case 0xCA:
                    trace($"call :: sum 0x{r2:X}, 0x{r3:X}");
                    if (ff)
                        mem[r1] = f32i64 & (i64f32 & mem[r2]) + (i64f32 & mem[r3]);
                    else
                        mem[r1] = mem[r2] + mem[r3];
                    break;
                case 0xCB:
                    trace($"call :: sub 0x{r2:X}, 0x{r3:X}");
                    if (ff)
                        mem[r1] = f32i64 & (i64f32 & mem[r2]) - (i64f32 & mem[r3]);
                    else
                        mem[r1] = mem[r2] - mem[r3];
                    break;
                case 0xCC:
                    trace($"call :: div 0x{r2:X}, 0x{r3:X}");
                    _ = (mem[r3], ff) switch {
                        (0x0, _    ) => bus.cpu.halt(0xC),
                        (_  , false) => mem[r1] = mem[r2] / mem[r3],
                        (_  , true ) => mem[r1] = f32i64 & (i64f32 & mem[r2]) / (i64f32 & mem[r3])
                    };
                    break;
                case 0xCD:
                    trace($"call :: mul 0x{r2:X}, 0x{r3:X}");
                    if (ff)
                        mem[r1] = f32i64 & (i64f32 & mem[r2]) * (i64f32 & mem[r3]);
                    else
                        mem[r1] = mem[r2] * mem[r3];
                    break;
                
                case 0xCE:
                    trace($"call :: pow 0x{r2:X}, 0x{r3:X}");
                    if (ff)
                        mem[r1] = f32i64 & Pow(i64f32 & mem[r2], i64f32 & mem[r3]);
                    else
                        mem[r1] = (uint)Math.Pow(mem[r2], mem[r3]);
                    break;
                case 0xCF:
                    trace($"call :: sqrt 0x{r2:X}");
                    if (ff)
                        mem[r1] = f32i64 & Sqrt(i64f32 & mem[r2]);
                    else
                        mem[r1] = (uint)Math.Sqrt(mem[r2]);
                    break;
                
                #endregion
                default:
                    bus.cpu.halt(0xFC);
                    Error($"call :: unknown opCode -> {iid:X2}");
                    break;
            }
            /* @break :: after */
            if (mem[0x17] == 0x3) mem[0x17] = 0x2;
            if (mem[0x17] == 0x2) 
            {
                bus.debugger.handleBreak(u16 & pc, bus.cpu);
                mem[0x17] = 0x0;
            }
            /* @break :: end */
        }

        public class Evaluate
        {
            private Stack<object> stack;
            private List<object> table;

            public object this[int index]
            {
                get => table[index];
                set => table[index] = value;
            }

            public void Prune()
            {
                stack?.Clear();
                table?.Clear();
                stack = null;
                table = null;
            }

            public void Alloc(int len)
            {
                stack = new Stack<object>(len);
                table = new List<object>(len);
            }
        }

        public readonly Evaluate evaluation = new Evaluate();

        private void CallAndWrite(ExternSignature info)
        {
            if (info.IsArgs())
            {
                bus.cpu.halt(0xA22, "not implemented");
                return;
            }
            try
            {
                var result = info.method.Invoke(null, new object[0]);
                if(!info.IsVoid())
                    stack.push(result.To<long>());
            }
            catch (Exception e)
            {
                bus.cpu.halt(0xA22, e.Message);
            }
        }
    }
}
