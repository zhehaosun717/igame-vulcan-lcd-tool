// 动画预览：缓存 getPreview 的结果，并记录"临时预览"的文件（在图片页点某个文件时，孪生屏先播放它）。
import { ref } from 'vue'
import { bridge } from '../bridge'
import type { PreviewInfo } from '../bridge/types'

const cache = new Map<string, Promise<PreviewInfo>>()

/** 读取某个屏幕文件的预览（同一会话内缓存；失败不缓存，下次重试） */
export function loadPreview(name: string): Promise<PreviewInfo> {
  let p = cache.get(name)
  if (!p) {
    p = bridge.call('getPreview', { name }).catch((error: unknown) => {
      cache.delete(name)
      return { available: false, reason: error instanceof Error ? error.message : '读取预览失败' }
    })
    cache.set(name, p)
  }
  return p
}

/** 上传完成等场景：文件内容可能变了，丢掉缓存 */
export function forgetPreview(name?: string): void {
  if (name) cache.delete(name)
  else cache.clear()
}

/** 临时预览的文件名；为空表示孪生屏显示屏幕"此刻"的内容 */
export const peekName = ref('')
let peekTimer: ReturnType<typeof setTimeout> | undefined

/** 在孪生屏上临时预览某个文件，12 秒后自动回到当前内容 */
export function peek(name: string): void {
  peekName.value = name
  if (peekTimer) clearTimeout(peekTimer)
  peekTimer = setTimeout(() => (peekName.value = ''), 12000)
}
export function endPeek(): void {
  if (peekTimer) clearTimeout(peekTimer)
  peekName.value = ''
}
