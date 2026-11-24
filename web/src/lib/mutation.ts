import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'

/**
 * A mutation that shows a success or error notification and refreshes affected data.
 * `invalidate` lists query-key prefixes to refetch, e.g. ['loans', 'accounts'].
 */
export function useApiMutation<TVariables, TResult>(
  fn: (variables: TVariables) => Promise<TResult>,
  options: {
    success?: string | ((result: TResult, variables: TVariables) => string)
    invalidate?: string[]
    onSuccess?: (result: TResult, variables: TVariables) => void
  } = {},
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: async (result, variables) => {
      await Promise.all((options.invalidate ?? []).map((key) => queryClient.invalidateQueries({ queryKey: [key] })))
      if (options.success) {
        const message = typeof options.success === 'function' ? options.success(result, variables) : options.success
        notifications.show({ color: 'green', message })
      }
      options.onSuccess?.(result, variables)
    },
    onError: (error) => {
      notifications.show({ color: 'red', title: 'Not done', message: error.message, autoClose: 8000 })
    },
  })
}
