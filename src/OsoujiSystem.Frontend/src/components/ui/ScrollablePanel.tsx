import { type ReactNode } from 'react'
import { GlassPanel } from './GlassPanel'
import { joinClassNames } from './utils'

export interface ScrollablePanelProps {
  children: ReactNode
  className?: string
  header?: ReactNode
  headerClassName?: string
  bodyClassName?: string
  bodyTestId?: string
  footer?: ReactNode
  footerClassName?: string
}

export function ScrollablePanel({
  children,
  className,
  header,
  headerClassName,
  bodyClassName,
  bodyTestId,
  footer,
  footerClassName,
}: ScrollablePanelProps) {
  return (
    <GlassPanel className={joinClassNames('flex min-h-0 flex-col gap-4', className)}>
      {header ? <div className={joinClassNames('shrink-0', headerClassName)}>{header}</div> : null}
      <div data-testid={bodyTestId} className={joinClassNames('min-h-0', bodyClassName)}>
        {children}
      </div>
      {footer ? <div className={joinClassNames('shrink-0', footerClassName)}>{footer}</div> : null}
    </GlassPanel>
  )
}