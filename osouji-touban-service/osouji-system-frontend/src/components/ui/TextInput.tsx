import { type InputHTMLAttributes } from 'react'
import { joinClassNames } from './utils'

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={joinClassNames('field-shell', props.className)} />
}
