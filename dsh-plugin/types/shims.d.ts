// 最小类型垫片：编译期只需要这两个模块的形状，运行期由 DSH 与 Node 提供。
declare module 'node:net' {
  export interface Socket {
    once(event: 'connect', listener: () => void): void
    once(event: 'error', listener: (error: Error) => void): void
    write(data: string, callback?: () => void): boolean
    end(): void
    destroy(): void
  }
  export function connect(path: string): Socket
}

declare module '@deepseek-ai/cordis' {
  export interface Context {
    on(name: string, listener: (...args: any[]) => unknown): () => void
    effect(fn: () => () => void): () => void
  }
}
