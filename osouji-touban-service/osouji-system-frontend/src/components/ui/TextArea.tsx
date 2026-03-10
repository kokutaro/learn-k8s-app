import { type TextareaHTMLAttributes } from 'react'
import { joinClassNames } from './utils'

export function TextArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={joinClassNames('field-shell min-h-28', props.className)} />
}
