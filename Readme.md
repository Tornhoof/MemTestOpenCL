Quick & Dirty attempt for a Memtest for GPUs via OpenCL
Only Tested on NVidia RTX.
1. How does it work?
- Attempts to allocate as much memory as possible
- Fills the memory with byte patterns
- Reads the patterns out again and compares them to the input
2. Run it often enough (default is 10 times) and if you really have bad VRAM it should produce an error

This is not maintained, this is just a quick & dirty attempt to check if a gpu of mine was bad.
