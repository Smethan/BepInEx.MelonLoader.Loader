claude plugin marketplace add wshobson/agents && \
for plugin in systems-programming debugging-toolkit backend-development code-review-ai comprehensive-review performance-optimization git-pr-workflows tdd-workflows cicd-automation incident-response distributed-debugging observability-monitoring code-documentation api-documentation; do \
  claude plugin install $plugin; \
done