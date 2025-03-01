template<typename T>
struct RCP {  //A device which is part of RCP
  const u32 DefaultReadCycles = 20;
  const u32 DefaultWriteCycles = 0;  //not implemented until we implement the CPU write queue

  template<u32 Size>
  auto read(u32 address, u32& cycles) -> u64 {
    cycles = DefaultReadCycles;
    if constexpr(Size == Byte) {
      auto data = ((T*)this)->readWord(address, cycles);
      switch(address & 3) {
      case 0: return data >> 24;
      case 1: return data >> 16;
      case 2: return data >>  8;
      case 3: return data >>  0;
      }
    }
    if constexpr(Size == Half) {
      auto data = ((T*)this)->readWord(address, cycles);
      switch(address & 2) {
      case 0: return data >> 16;
      case 2: return data >>  0;
      }
    }
    if constexpr(Size == Word) {
      return ((T*)this)->readWord(address, cycles);
    }
    if constexpr(Size == Dual) {
      u64 data = ((T*)this)->readWord(address, cycles);
      return data << 32 | ((T*)this)->readWord(address + 4, cycles);
    }
    unreachable;
  }

  template<u32 Size>
  auto write(u32 address, u64 data, u32& cycles) -> void {
    cycles = DefaultWriteCycles;
    if constexpr(Size == Byte) {
      switch(address & 3) {
      case 0: return ((T*)this)->writeWord(address, data << 24, cycles);
      case 1: return ((T*)this)->writeWord(address, data << 16, cycles);
      case 2: return ((T*)this)->writeWord(address, data <<  8, cycles);
      case 3: return ((T*)this)->writeWord(address, data <<  0, cycles);
      }
    }
    if constexpr(Size == Half) {
      switch(address & 2) {
      case 0: return ((T*)this)->writeWord(address, data << 16, cycles);
      case 2: return ((T*)this)->writeWord(address, data <<  0, cycles);
      }
    }
    if constexpr(Size == Word) {
      ((T*)this)->writeWord(address, data, cycles);
    }
    if constexpr(Size == Dual) {
      ((T*)this)->writeWord(address, data >> 32, cycles);
    }
  }
};

template<typename T>
struct PI {  //A device which is reachable only behind PI
  template<u32 Size>
  auto read(u32 address) -> u64 {
    static_assert(Size == Half || Size == Word);  //PI bus will do 32-bit (CPU) or 16-bit (DMA) only
    if constexpr(Size == Half) {
      return ((T*)this)->readHalf(address);
    }
    if constexpr(Size == Word) {
      return ((T*)this)->readWord(address);
    }
    unreachable;
  }
  
  template<u32 Size>
  auto write(u32 address, u64 data) -> void {
    static_assert(Size == Half || Size == Word);  //PI bus will do 32-bit (CPU) or 16-bit (DMA) only
    if constexpr(Size == Half) {
      return ((T*)this)->writeHalf(address, data);
    }
    if constexpr(Size == Word) {
      return ((T*)this)->writeWord(address, data);
    }
    unreachable;
  }
};

template<typename T>
struct SI {  //A device which is reachable only behind SI
  template<u32 Size>
  auto read(u32 address) -> u64 {
    static_assert(Size == Word);  //SI bus will do 32-bit (CPU/DMA)
    if constexpr(Size == Word) {
      return ((T*)this)->readWord(address);
    }
    unreachable;
  }
  
  template<u32 Size>
  auto write(u32 address, u64 data) -> void {
    static_assert(Size == Word);  //PI bus will do 32-bit (CPU/DMA)
    if constexpr(Size == Word) {
      return ((T*)this)->writeWord(address, data);
    }
    unreachable;
  }
};
