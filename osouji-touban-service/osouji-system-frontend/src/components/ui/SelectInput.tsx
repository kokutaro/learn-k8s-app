import { type SelectHTMLAttributes } from 'react'
import { joinClassNames } from './utils'

export function SelectInput(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={joinClassNames('field-shell', props.className)} />
}
